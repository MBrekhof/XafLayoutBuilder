# How XafLayoutBuilder works

For XAF developers. What happens at each layer, why it is built this way, and where XAF pushed
back. The verified references (file and line in the installed 26.1 sources, dxdocs pages) are in
[api-notes.md](api-notes.md); this document explains, that one proves.

## The idea

XAF's default views come from node generators that run when the Application Model is built.
Everything anyone customises afterwards is a difference layer on top of that generated layer:
module XAFML, the administrator's shared differences, each user's own differences.
XafLayoutBuilder writes the business class's layout into the generated layer, through the hook XAF
offers for exactly that: `ModelNodesGeneratorUpdater<T>`. The builder therefore does not compete
with the Model Editor or with runtime customisation. It replaces the default they start from.

| Layer | Written by | Relation to the builder |
|---|---|---|
| Generated (zero) | XAF node generators, then registered generator updaters | The builder output lives here |
| Module differences | `Model.DesignedDiffs.xafml` of each module | Applies on top, not reconciled |
| Shared differences | `Model.xafml` or the administrator's store | Applies on top |
| User differences | `ModelDifferences` rows per user | Applies on top |

Consequences, all deliberate: builder changes need a restart; an administrator or user who changed
something keeps it; deleting their differences shows the builder layout again (E2E 4 to 6).

## Three parts

```
Fluent builder ──► LayoutSpec (immutable records, JSON) ──► generator updaters ──► Application Model
                          ▲
                          └── LayoutExporter ◄── merged model, every layer ──► CSharpLayoutPrinter ──► C#
```

- **XafLayoutBuilder.Core** has no DevExpress reference: builders, spec records, validation, JSON
  and the C# printer. Other tools can emit or read specs without dragging XAF along.
- **XafLayoutBuilder.Module** references `DevExpress.ExpressApp` and `DevExpress.Persistent.Base`
  only, so it is platform neutral: the two updaters, discovery and registry, the startup check, the
  exporter and the export action.
- **The spec is the contract.** Builders validate at `Build()` and hand out frozen records. Every
  list is copied into a read-only collection in its `init` accessor, so the constructor, JSON
  deserialisation and `with` expressions all end with a private copy.

## DetailView: `DetailViewLayoutUpdater`

It is a `ModelNodesGeneratorUpdater<ModelDetailViewLayoutNodesGenerator>`. XAF calls it with the
`Layout` node right after the stock generator filled it; the parent is the `IModelDetailView`. Only
`{Type}_DetailView` is handled; variants and nested views are left alone.

The updater removes what the generator produced and rebuilds from the spec, following the stock
generator's own conventions so nothing downstream notices a difference:

- A root group `Main` (index 0, vertical, caption hidden) holds the spec's top-level nodes.
- A tab made with `TabFor(x => x.Lines)` becomes the same shape the generator builds for a
  collection: a tabbed group whose child is a group with id `Lines` and a visible caption, holding
  a layout item `Lines` with its caption hidden. Tabbed groups may only contain groups.
- A group that is `Collapsible()` always shows its caption. XAF Blazor renders the collapse toggle
  inside the caption header; a collapsible group without a caption renders with no toggle at all.
- Hidden members are simply not placed. The stock generator would place every visible editor, but
  the updater owns the node, so leaving a member out is enough.

Two checks run while applying:

- **XLB001**: a placed member has no entry in the view's `Items`, typically `[Browsable(false)]`.
- **XLB002**: a visible member is neither placed nor hidden. Strict on purpose, confirmed by the
  owner: a property added to the class must not silently vanish from the form.

That strictness is right for generated code and irritating for people, so a class can opt out with
`.Unplaced(UnplacedMembers.AppendToGroup("Other"))`. The applier then creates that group at the end
of the root and drops everything unmentioned into it, captioned with the group id. The caption is
set explicitly because XAF names a single-item group after its item, which would title an "Other"
group holding one leftover member after that member instead.

## ListView: `ListViewColumnsUpdater`

It is a `ModelNodesGeneratorUpdater<ModelListViewColumnsNodesGenerator>` and handles
`{Type}_ListView`, plus `{Type}_LookupListView` when the spec has a `Lookup(...)`.

Unlike the DetailView, the generated columns are kept. Listed columns get their order, width,
caption and sort. Every other column is set to index -1: not shown, still offered by the column
chooser. Any default sort on those columns is cleared, because the stock generator sorts the
display member ascending and that would fight the spec's own sort. Nothing can vanish here, so
there is no XLB002 equivalent.

Three details took the XAF sources to get right:

- **Column index goes through `GeneratedIndex`.** The stock generator does not leave a generated
  column's order in `Index`. It moves it into an internal value called `GeneratedIndex` and clears
  `Index`, and the column domain logic reads that value only while the administrator has not set
  `FreezeColumnIndices`. The updater does the same, so a frozen column set stays frozen when a new
  listed column appears. The value name is an internal constant repeated as a literal with a
  comment; if DevExpress renamed it, columns would fall back to their natural order and E2E 2 would
  fail on the header order.
- **Lookup views get almost no generated columns.** The lookup generator creates columns only for
  the friendly key, the display member and members visible in lookup views. A spec that lists
  `Customer` in its lookup would therefore find no column. The updater adds missing columns the way
  the generator does internally: `AddNode<IModelColumn>(name)` plus `PropertyName`. Hidden members
  get a column too, so the chooser can offer them.
- **XLB003** is raised for a collection or for a name that is not a member of the type.

## Discovery

`ISupportViewLayoutCustomization` has two static abstract members. The resolver reads them through
`Type.GetInterfaceMap` and caches each one separately per type, so a DetailView factory that throws
never costs the ListView its columns, and the startup check resolves each view's spec inside that
view's own attempt. A factory that throws is not cached. `LayoutRegistry.Register<T>` covers
types you do not own; registry entries win, and registration checks that every member the spec
names exists on `T`.

Static abstract implementations are inherited: `ServiceOrder : Order` maps the interface to
`Order`'s methods. The resolver only applies a spec whose `TypeName` is the exact type, so a
derived class keeps XAF's default layout. Composing a derived layout from its base is a phase 2
candidate.

## Startup check

XAF generates view nodes lazily, on first access. Left alone, a broken layout would surface when a
user first opens the view. The module hooks `XafApplication.SetupComplete` and, for every type with
a spec, reads `NodeCount` on the layout and columns nodes of the views the updaters actually handle,
looked up by their fixed ids: `{Type}_DetailView`, `{Type}_ListView` and, with a lookup spec,
`{Type}_LookupListView`. Reading the count generates the nodes, which runs the updaters, which
throw. Following `IModelClass.DefaultDetailView` instead would be wrong: a model difference can
repoint it at another view, and the check would then validate a view no spec applies to while
leaving the real one unchecked. A missing view is **XLB004**.

What happens next is the host's choice. With `XafLayoutBuilderModule.FailFastOnLayoutErrors` on,
the exception propagates: in the Blazor template the application is built while the ASP.NET host
starts, so it ends the process before Kestrel listens. With it off, the default, the updaters and
the check log the exception through XAF's `Tracing` and the view keeps XAF's own layout. That is
safe only because the updaters check a spec completely before they change anything; XAF marks a
node as generated even when an updater throws, so a failure halfway through would otherwise leave
a half-applied layout for good. The default is off because in a production host a startup failure
is not a screen-level failure: a Blazor host with model warm-up never starts, and a WinForms client
never opens. The E2E gate runs the sample's `--break-layout` fixture both ways.

XAF Blazor builds one `XafApplication` per circuit, so the naive version of this would repeat the
whole forced generation for every user session. A static set of what already passed makes it once
per process. Two details keep that memory honest: the key is the application type *and* the
registry version, which `LayoutRegistry` bumps on every `Register` or `Clear`, so a layout
registered after one application started is still validated for the next one; and with fail-fast
on only a completed run is recorded, so an application that fails the check keeps failing loudly
rather than passing quietly on the second session. With it off, a degraded run is recorded too:
ASP.NET Core shares one Application Model per process, so another circuit has nothing new to log. The run happens under a lock, so two circuits starting together do
the work once rather than twice. `LayoutStartupCheck.Reset()` clears the memory for tests.

## Exporter and printer

The **Export Layout To Code** action is a `ViewController<ObjectView>` in the Tools category. It is
active for administrators, and only with a debugger attached or `EnableExport` set by the host from
its configuration. A host with no security system has no roles to ask, so there the debugger or the
flag is the only gate. It exports the view it was invoked from and fills the other half from the
type's default views, all from the merged model with every layer applied, and names the view ids in
the printed comment.

- **Only values a layer stored are exported,** tested with `ModelNode.HasValue`. XAF computes most
  properties when nothing is stored, and those computed defaults must not turn into builder calls.
- **Captions are the exception.** `Caption` is localizable, and `HasValue` looks in the current
  language aspect while the generated value sits in the default aspect. The first export dropped
  the Header group's explicit caption for that reason. The exporter compares a caption with XAF's
  own default instead: for a group, the single item's caption or else the group id; for a column,
  its member's caption. A caption equal to that default is still printed when the group would
  otherwise lose its header, because the applier shows a caption only for a captioned, collapsible
  or tab group.
- **Member names come from `PropertyName`,** not from the node id. The two usually match, but only
  the property name is a CLR member that a lambda can name.
- **A customised root group is kept.** The stock root is one plain `Main` group and is unwrapped so
  its children become the spec's top level. A root group with its own direction, caption or size is
  a customisation and is exported as an ordinary group instead of being dropped.
- **What the builder cannot express is skipped,** not printed: a layout item that is not a property
  editor, and any member bound to a nested path such as `Customer.Name`. Each becomes a comment at
  the top of the exported file.
- **Hidden is inferred.** A visible member that is not placed is exported as `.Hide(...)`. A column
  without an index is exported as hidden, except the key; the model does not record whether the
  original builder hid a column or never mentioned it.
- **The catch-all group exports as the policy, not as its contents.** The applier stamps the group
  it creates for `.Unplaced(...)` with a model value, so the exporter can tell it from a group
  somebody wrote by hand. It emits `.Unplaced(UnplacedMembers.AppendToGroup(id))`, drops the group
  from the printed nodes and leaves its members out of the hidden list, because the policy collects
  them again. Freezing them into explicit items instead would silently restore strict XLB002 for
  the next property somebody adds.
- **The printer is a fixed point.** For the start document's example, builder to spec to printed
  C# reproduces the source byte for byte (unit test). For the running sample, exporting the
  untouched layout reproduces `Order.Layout.cs` modulo whitespace and explicit hides (E2E 5a).

The popup is a DetailView of the non-persistent `LayoutCode` with one unlimited string.

Getting the text out of the browser needs the browser, so that half lives in
`XafLayoutBuilder.Blazor`, an optional add-on module with two controllers.

- **Copy Layout To Clipboard** calls `navigator.clipboard.writeText` through XAF's own
  `IXafJSRuntime`, with no JavaScript file of its own. `IXafJSRuntime` is marked
  `EditorBrowsable(Never)`: one of the two DevExpress internals this repository leans on, the other
  being the generated column index.
- **Download Layout File** hands over `{Type}.Layout.cs`. A server-side action cannot start a
  download by itself and Chrome blocks top-level `data:` navigation, so the add-on ships one JS
  module in its `wwwroot` that creates an anchor, clicks it and revokes the blob URL. Served from
  the Razor class library at `_content/XafLayoutBuilder.Blazor/`, it is the only JavaScript here.

Both sit in the Tools tab next to the export rather than inside the popup, because XAF Blazor's
popup template for a non-persistent object renders only its own OK and Cancel buttons
(`PopupDialogTemplateBase` builds exactly one action container, "Confirmation"). All three paths
print through the same `LayoutCodePrinter.ForView`, so the popup, the clipboard and the file always
agree, and the gate asserts exactly that.

## The user layer in XAF Blazor

E2E 4 to 6 needed a user difference. XAF Blazor's layout editor moves elements only by drag and
drop, so the harness writes the difference the editor would persist. Three XAF behaviours shaped
that:

- `ModelDifferenceDbStore` keys the row by the security user id and the context `Blazor`; the
  aspect with an empty name holds the XAFML.
- EF Core deferred deletion filters on `GCRecord = 0`, so a row inserted with `GCRecord` NULL is
  invisible to XAF.
- XAF Blazor saves the user model through a deferred dispatcher that flushes its in-memory copy
  over the row before loading it again. A row written from outside only counts for a fresh process,
  so the harness stops the host, writes, and starts it again.

Moving an item in a user layer looks like this:

```xml
<DetailView Id="Order_DetailView">
  <Layout>
    <LayoutGroup Id="Main">
      <LayoutGroup Id="Header">
        <LayoutItem Id="OrderDate" Removed="True" />
      </LayoutGroup>
      <LayoutGroup Id="Details">
        <LayoutItem Id="OrderDate" ViewItem="OrderDate" Index="1" IsNewNode="True" />
      </LayoutGroup>
    </LayoutGroup>
  </Layout>
</DetailView>
```

One XAF quirk shows up after that move: the Details group keeps the caption "Notes" it derived
when Notes was its only item. The export prints what renders, so it emits `.Caption("Notes")`.

## Testing

- **Unit tests** cover Core: every builder rule, immutability through every construction path,
  JSON round trips and the printer. The updaters and the exporter need a live Application Model, so
  they are tested only through the E2E gate.
- **The E2E gate** is a console app with C# Playwright. It builds and starts the sample on port
  5100, refuses to run if something already serves there, and walks E2E 1 to 6 from the start
  document plus the round-trip and startup checks. The file header lists every assertion.

## Decisions and where they came from

- XLB002 strict placement: proposed in session 3, confirmed by the owner.
- Derived classes keep XAF's default layout: session 3, consistent with the start document's
  phase 2 list.
- Column index through `GeneratedIndex`: a Codex review found that storing `Index` directly broke
  `FreezeColumnIndices` for columns added later.
- Sibling-only id uniqueness, frozen specs and the registry member check: Codex review of sessions
  2 and 3.
- E2E 4 writes user XAFML instead of driving the drag-and-drop editor: session 6, for effort.
- No copy button in the export popup: session 6, platform neutrality of the module.
- No `ModelNodesGenerator` subclasses were needed, so `BACKBURNER.md` does not exist.
