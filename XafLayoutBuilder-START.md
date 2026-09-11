# XafLayoutBuilder — Start Document

Typed, compile-checked C# for DevExpress XAF view layouts, poured into the Application Model
as generated-layer defaults. The visual designer stays the running Blazor app; the source of
truth becomes a fluent builder next to the business class, not `Model.xafml`.

Status: POC. One subject, one E2E gate, README states the limitations.

---

## 1. The question this repo answers

Can a business class declare its DetailView layout and ListView columns in fluent C#, have that
flow into `IModelDetailView.Layout` / `IModelListView.Columns` through ordinary
`ModelNodesGeneratorUpdater`s so that admin and user model differences still win on top, and
can the running app export any view's current layout back to the same fluent C#?

If yes: AI agents get a two-page API instead of XAFML, member references become compile errors
instead of runtime surprises, and layout changes are reviewable diffs in git.

Origin: DevExpress Support Center ticket T1206756 (Model Editor / non-Windows development),
Gat's comment of 2026-09-11 describing a closed-source implementation. This is the open one.

---

## 2. Scope

### In

- `LayoutBuilder<T>` for DetailView layout: groups, tabbed groups, items, flow direction,
  captions, collapsible, hidden items, relative size.
- `ListViewColumnsBuilder<T>` for ListView columns: column order, sort order, width, hidden
  columns, a separate column set for the lookup ListView.
- Discovery by convention (`ISupportViewLayoutCustomization` with static abstract members) and
  explicit registration for types you don't own (`LayoutRegistry.Register<T>(...)`).
- Two generator updaters that apply the builder output to the generated model layer.
- An admin-only `Export Layout To Code` action on DetailView and ListView that emits the fluent
  C# for the view as it currently renders (all layers applied).
- A `SKILL.md` for Claude Code that documents the API surface with one example. The skill is
  half the product.
- Sample module (two entities, one inheritance pair) + Blazor Server host.
- Playwright E2E gate.

### Out (say so in the README)

- Localization. Second repo.
- Bands in ListView columns. Phase 2 at the earliest; the model API for bands needs checking
  against 26.1 first (see §7).
- WinForms. The updaters are platform-neutral so it should work; not tested, not claimed.
- Class hierarchy composition (derived class extends base layout). Phase 2.
- Any runtime editing. This repo has no chat, no MCP, no AI at runtime. It gives agents a
  target; it doesn't host one.
- Conflict resolution between builder output and existing module XAFML for the same view. The
  builder replaces the generated layout for that view; module-layer XAFML for that view still
  applies on top. Document it, don't solve it.

---

## 3. Architecture

Three layers, deliberately separated so BPG can reuse the middle one:

```
Fluent builder  ──►  LayoutSpec (immutable IR)  ──►  Model applier (generator updaters)
                                 ▲
                                 │
Running view (IModelDetailView)  ┘  ◄── Exporter walks model → LayoutSpec → C# printer
```

- **Builder** — the thing humans and agents write. Lambdas for members. Produces a `LayoutSpec`.
- **LayoutSpec** — plain records: `DetailLayoutSpec`, `LayoutGroupSpec`, `TabbedGroupSpec`,
  `LayoutItemSpec`, `ListColumnsSpec`, `ColumnSpec`. No XAF types. Serialisable to JSON.
  This is the contract BPG emits against, and what the exporter produces before printing code.
- **Applier** — `DetailViewLayoutUpdater : ModelNodesGeneratorUpdater<ModelDetailViewLayoutNodesGenerator>`
  and `ListViewColumnsUpdater : ModelNodesGeneratorUpdater<ModelListViewColumnsNodesGenerator>`,
  registered in `ModuleBase.AddGeneratorUpdaters`. They find the spec for the view's
  `ModelClass.TypeInfo.Type`, clear the generated layout/columns, and rebuild from the spec.
- **Exporter** — a `ViewController` action, visible only when `Debugger.IsAttached` or
  `XafLayoutBuilder:EnableExport=true`. Walks `View.Model` (the merged model, all layers),
  builds a `LayoutSpec`, prints C# via a small `CSharpLayoutPrinter`, shows it in a popup with
  a copy button. It writes nothing to disk. Saving to source is the developer's job; that's the
  point.

Project layout:

```
XafLayoutBuilder/
├── XafLayoutBuilder.Core/            # builder, spec, printer — no XAF dependency beyond
│                                     # DevExpress.ExpressApp for IModel* interfaces
├── XafLayoutBuilder.Module/          # updaters, registry, export controller
├── XafLayoutBuilder.Sample.Module/   # Customer, Order, ServiceOrder : Order
├── XafLayoutBuilder.Sample.Blazor.Server/
├── XafLayoutBuilder.Tests/           # xUnit: builder → spec, spec → printer round trip
├── XafLayoutBuilder.E2ETests/        # Playwright console app, same pattern as XafReportScheduler
└── skills/xaf-layout-builder/SKILL.md
```

---

## 4. Target API surface

This is what the SKILL.md documents. Keep it this small; every extra method is a thing the
agent can get wrong.

```csharp
public partial class Order : BaseObject, ISupportViewLayoutCustomization
{
    public static DetailLayoutSpec BuildDetailViewLayout() =>
        LayoutBuilder<Order>.Create()
            .Group("Header", g => g
                .Caption("Order")
                .Flow(FlowDirection.Horizontal)
                .Item(x => x.Number)
                .Item(x => x.Customer)
                .Item(x => x.OrderDate))
            .Group("Details", g => g
                .Collapsible()
                .Item(x => x.Notes, relativeSize: 100))
            .Tabs("Tabs", t => t
                .TabFor(x => x.Lines, imageName: "BO_Order_Item")
                .TabFor(x => x.Attachments))
            .Hide(x => x.SyncToken)
            .Build();

    public static ListColumnsSpec BuildListViewColumns() =>
        ListViewColumnsBuilder<Order>.Create()
            .Column(x => x.Number, width: 90)
            .Column(x => x.Customer)
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
            .Hide(x => x.SyncToken)
            .Lookup(l => l
                .Column(x => x.Number)
                .Column(x => x.Customer))
            .Build();
}
```

Rules the builder enforces at build time (throw, don't warn):

- An item/column referenced twice.
- An item referenced that is also hidden.
- A group id used twice in the same view.
- A lambda that is not a simple member access (`x => x.Customer.Name` is rejected in the POC;
  nested paths are phase 2).

Rules enforced at startup by the applier (throw with a numbered diagnostic, fail fast):

- Member exists in `ITypesInfo` for `T` but has no `IModelDetailView.Items` entry (e.g.
  `[Browsable(false)]`). Message names the member and the view id.

Discovery:

```csharp
public interface ISupportViewLayoutCustomization
{
    static abstract DetailLayoutSpec BuildDetailViewLayout();
    static abstract ListColumnsSpec BuildListViewColumns();
}
```

Static abstract members mean a class that opts in must implement both; if only one is wanted,
return `null` and the applier leaves that view alone. For foreign types:

```csharp
LayoutRegistry.Register<ReportDataV2>(
    detail: LayoutBuilder<ReportDataV2>.Create()....Build(),
    columns: null);
```

Registry entries win over interface discovery for the same type.

---

## 5. Applier details (where the real work is)

`DetailViewLayoutUpdater.UpdateNode(ModelNode node)`:

1. `node` is `IModelViewLayout`; `node.Parent` is `IModelDetailView`; the type is
   `view.ModelClass.TypeInfo.Type`. Only handle the default DetailView id
   (`{TypeName}_DetailView`); skip variants and nested/custom ids in the POC.
2. Resolve the spec: registry first, then `ISupportViewLayoutCustomization` via reflection on
   the type (call the static member through the interface).
3. `node.ClearNodes()`, then rebuild: root `IModelLayoutGroup` "Main" →
   `AddNode<IModelLayoutGroup>(id)` / `AddNode<IModelTabbedGroup>(id)` /
   `AddNode<IModelLayoutViewItem>(id)` with `ViewItem = view.Items[memberName]`.
4. Set `Direction`, `Caption`, `ShowCaption`, `IsCollapsibleGroup`, `RelativeSize`,
   `ImageName` from the spec.
5. Hidden items: simply not added. XAF's layout generator would otherwise add every
   `IModelDetailView.Items` entry; because we own the generated layout node, omission is enough.

`ListViewColumnsUpdater.UpdateNode(ModelNode node)`:

1. `node` is `IModelColumns`; parent is `IModelListView`. Handle `{TypeName}_ListView` and
   `{TypeName}_LookupListView`; the lookup gets the `.Lookup(...)` spec if present, else is left
   to XAF.
2. Rebuild columns in spec order; set `Index`, `Width`, `SortOrder`, `SortIndex`. Hidden
   columns are added with `Index = -1` (XAF convention for "available but not shown"), so the
   user can still turn them on in the column chooser. That's a deliberate difference from
   DetailView hiding; document it.

Layer semantics to state explicitly in the README: updaters run when the generated layer is
built. Changes to the builder code appear after restart. Anything an administrator or user has
changed in the model differences store still overrides the builder. The builder replaces the
*default*, nothing else.

---

## 6. Exporter details

`ExportLayoutController` (DetailView + ListView, `TargetViewType = Any`):

1. Take `View.Model` as `IModelDetailView` / `IModelListView`.
2. Walk `Layout` recursively → `DetailLayoutSpec`; walk `Columns` ordered by `Index` →
   `ListColumnsSpec`. Items whose `ViewItem` is null or not a `IModelPropertyEditor` are
   skipped with a comment in the output (`// skipped: ActionContainer "Save"`).
3. `CSharpLayoutPrinter.Print(spec, typeName)` emits exactly the API from §4, formatted with
   one call per line, so the diff against the existing builder is readable.
4. Show in a popup (`PopupWindowShowAction` with a non-persistent object holding the text) with
   a copy button. Optionally, and only if a source path is configured, offer "write to
   `{TypeName}.Layout.cs`" behind a confirmation. Default off.

Round-trip property the tests assert: builder → apply → export → print yields the same builder
code (modulo formatting) for the sample entities.

---

## 7. Things to verify against 26.1 before writing code

Do this with the dxdocs MCP server, not from memory. Names below are from memory and may have
drifted.

- Exact generator types: `ModelDetailViewLayoutNodesGenerator`,
  `ModelListViewColumnsNodesGenerator`. Confirm the `UpdateNode` signature and that the node
  passed is the layout root / columns node.
- `IModelLayoutGroup`, `IModelTabbedGroup`, `IModelLayoutViewItem` property names
  (`Direction`, `IsCollapsibleGroup`, `RelativeSize`, `ShowCaption`, `ImageName`).
- Whether `IModelTabbedGroup` children must be `IModelLayoutGroup` (yes, I believe) and how
  a collection property tab is represented (`IModelLayoutViewItem` with `ViewItem` pointing to
  the `IModelPropertyEditor` for the collection).
- Column hiding convention (`Index = -1`) is still honoured by `DxGridListEditor`.
- Bands: `IModelListView.BandsLayout` / `IModelBand` — check before promising phase 2.
- `ModelNodesGeneratorUpdater` registration timing vs `CustomizeTypesInfo`; the type must be
  fully registered before the updater runs.

Write findings into `docs/api-notes.md` as you go. That file is also skill material.

---

## 8. E2E gate

Same shape as XafReportScheduler: a Playwright console app that builds, starts the sample on
port 5100, and asserts against the real UI. Exit 0/1/2.

Assertions:

1. `Order_DetailView` renders the groups in builder order; `SyncToken` is not present in the
   DOM; `Notes` is inside a collapsible group.
2. `Order_ListView` shows columns `Number, Customer, OrderDate` in that order, sorted by
   OrderDate descending; `SyncToken` is absent but present in the column chooser.
3. `Order_LookupListView` (open a `ServiceOrder` detail, use the lookup) shows only
   `Number, Customer`.
4. Log in as Admin, use the runtime layout customisation to move `OrderDate` into the
   `Details` group, save. Reload: user layer wins, `OrderDate` is in `Details`.
5. Click `Export Layout To Code` on that view. The exported text contains
   `.Group("Details"` followed by `.Item(x => x.OrderDate)`. That's the Andreas case: the
   designer is the app, the output is code.
6. Reset the user model (delete the `ModelDifference` rows). Reload: builder layout is back.

Unit tests (xUnit): builder validation rules from §4, spec JSON round trip, printer output for
a fixed spec, exporter → printer → builder equivalence on the sample specs.

---

## 9. Session plan

Small sessions, each ending green. Update `SESSION_HANDOFF.md` at the end of each one.

1. Solution skeleton, sample module with `Customer`, `Order`, `ServiceOrder : Order`, Blazor
   host running with default XAF layouts. E2E harness starts and logs in. Nothing custom yet.
2. `XafLayoutBuilder.Core`: builder, spec records, validation, unit tests. No XAF yet.
3. `DetailViewLayoutUpdater` against the sample. Verify §7 items as you hit them. E2E 1.
4. `ListViewColumnsUpdater` including lookup. E2E 2–3.
5. Registry + interface discovery + startup diagnostics. Deliberately break a member name
   and assert the startup error message.
6. Exporter + printer + popup. E2E 4–6, round-trip unit test.
7. `SKILL.md`, README (limitations section written honestly), `docs/how-it-works.md`,
   screenshots in `docs/screenshots/`.

Phase 2 candidates, not in this POC: hierarchy composition (`Extend<TBase>()`), bands, nested
member paths, localised captions via message keys, a `spec.json` loader so BPG can ship
layouts as data instead of code.

---

## 10. Working agreements for Claude Code

- Verify DevExpress API names via the dxdocs MCP server before use. Don't guess XAF internals.
- Use the xaf-hardlearned skills (Blazor thread safety, EF Core proxies) when touching the
  sample module or the export popup.
- No `ModelNodeGenerator` subclasses. Updaters only. If it seems to need a generator, stop and
  write down why in `BACKBURNER.md`.
- No XAFML in the sample module beyond what the template ships. The whole point is that the
  sample has none for `Order`.
- Every session ends with `dotnet build`, unit tests green, and the E2E gate at its current
  expected level. Don't advance the session plan on a red gate.
- Keep the builder API to §4. A new method needs a sentence in `SKILL.md` and a test, or it
  doesn't go in.

---

## 11. Relationship to other repos

- **XafMergerTool** — stays as the answer for stock XAF where XAFML is the only source form.
  Gains an "export as builder C#" option once this exists, by referencing `XafLayoutBuilder.Core`.
- **XafMcp** — could expose `LayoutSpec` read/write as tools later. Not here.
- **BPG** — emits `LayoutSpec` (as generated C# via the printer, or as JSON in phase 2) from
  the spec's view section. Compile-checked layouts in generated apps; readable by the
  customer's developer; regenerated deterministically.
- **xafskills** — `skills/xaf-layout-builder/SKILL.md` is copied there on release.
