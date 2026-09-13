---
name: xaf-layout-builder
description: Declare a DevExpress XAF class's DetailView layout and ListView columns in fluent C# with XafLayoutBuilder instead of Model.xafml. Use when adding or changing the layout of an XAF business class, or adding a property to a class that already has a layout, in a solution that references XafLayoutBuilder.Module.
---

# XafLayoutBuilder

Typed, compile-checked layout for XAF (26.1, EF Core, Blazor tested). The builder output becomes
the generated (zero) layer of the Application Model. Module XAFML, the administrator's shared
differences and each user's differences still apply on top: the builder replaces the *default*,
nothing else. Changes appear after an application restart.

## Setup (once per solution)

- The app's module references the `XafLayoutBuilder.Module` package (a Blazor host also
  `XafLayoutBuilder.Blazor`; local feed, see the repository README) or the projects, and requires it:
  `RequiredModuleTypes.Add(typeof(XafLayoutBuilder.Module.XafLayoutBuilderModule));`
- Keep `XafLayoutBuilderModule` in its own assembly: reference the projects, never copy their files
  into an existing module project. `ModuleBase` scans its own assembly for `ModuleUpdater` types and
  model difference resources, so a copied module re-runs that project's database updaters and reads
  its model resources a second time.
- Do not create `Model.xafml` or `Model.DesignedDiffs.xafml` entries for views a builder owns.
- Before converting a view that already exists in use, check `ModelDifference` rows and module XAFML
  for it. Differences target the stock node paths (`Main/SimpleEditors/...`), which the builder
  replaces, so those customisations do not carry over: XAF ignores them, and the database user store
  deletes them at that user's next model save. A `FreezeLayout` copy does carry over.

## Declare a layout

Implement `ISupportViewLayoutCustomization` in a partial `{Type}.Layout.cs` next to the class.
Both static members are required; return `null` from one to leave that view to XAF.

```csharp
using XafLayoutBuilder.Core;

public partial class Order : ISupportViewLayoutCustomization
{
    public static DetailLayoutSpec? BuildDetailViewLayout() =>
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

    public static ListColumnsSpec? BuildListViewColumns() =>
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

Keep that file's only using `XafLayoutBuilder.Core`. `FlowDirection` and `ColumnSortOrder` also
exist in `DevExpress.ExpressApp.Layout` and `DevExpress.Data`; importing those in the same file
makes the names ambiguous.

For a type you do not own, register in your module's constructor:
`LayoutRegistry.Register<ReportDataV2>(detail, columns);` Registry entries win over the interface.
Nothing is checked at registration: a spec that names a member the type does not have, or breaks a
build-time rule, is reported when the view is built, under `FailFastOnLayoutErrors` like any other
layout error. When building the spec can throw, register factories instead
(`LayoutRegistry.Register<T>(() => ..., () => ...)`), so that exception is governed the same way; a
`Build()` written directly in the arguments throws in your own startup code.

A layout kept as data registers with `LayoutRegistry.RegisterJson<T>(() => File.ReadAllText(path))`:
one `LayoutSpecs` document (`LayoutSpecJson.Serialize(new LayoutSpecs(detail, columns))`, or the
file Download Layout JSON saves). The JSON is read when a view is built, each half on its own, so
malformed JSON or a half whose `typeName` is another type is a layout error like the others.

A further DetailView or ListView of a class, with its own id, is declared in code before the
application model is built: `LayoutRegistry.AddDetailView<Order>("Order_Compact_DetailView", () =>
LayoutBuilder<Order>.Create()...Build())` or `AddListView<Order>(id, () => ...)`. The module adds the
view to the generated layer, so navigation items, view variants and `ShowViewParameters` can open it
by id, and its spec is checked at startup like the default views'. A `Lookup(...)` in a declared
ListView's spec is ignored. Do not try to give a builder layout to a view that exists only in XAFML
(a Model Editor clone): XAF never runs the layout or columns generator for it. The class's
`{Type}_ListView` spec also shapes every nested ListView of the class (see the ListView surface).

## DetailView surface

- `LayoutBuilder<T>.Create()`, then `.Group(id, g => ...)`, `.Tabs(id, t => ...)`, `.Hide(x => x.M)`,
  `.Build()`. `.Item(x => x.M)` directly on the builder places an editor under the root, outside
  any group; exports produce it when a user dragged an editor out of every group.
- In a group: `.Caption("...")`, `.Flow(FlowDirection.Horizontal)` (default is vertical),
  `.Collapsible()`, `.RelativeSize(percent)`, `.Image("ImageName")`,
  `.Item(x => x.M, relativeSize: null)`, and nested `.Group(...)` and `.Tabs(...)`.
- In tabs: `.TabFor(x => x.Collection, imageName: null, caption: null)` makes one tab holding that
  member. `.Tab(id, g => ...)` makes a tab with arbitrary group content.
- `Collapsible()` always shows the group caption, because XAF Blazor puts the toggle in the caption
  header. A group without an explicit caption and with one item shows that item's caption.
- `Hide` removes the editor from the DetailView entirely.
- Every visible member must be placed or hidden, or the layout is rejected with XLB002 naming it. To opt out
  for one class, `.Unplaced(UnplacedMembers.AppendToGroup("Other"))` puts everything the layout does
  not mention into a group with that id at the end of the form. The strict default is deliberate:
  it is what stops a new property from disappearing unnoticed.
- Member lambdas are simple member access: `x => x.Customer`. A column may follow references:
  `Column(x => x.Customer.City)` and `Hide(x => x.Customer.City)` store `Customer.City`, the path XAF's
  own generator uses for such a column, and every segment must exist on its type. Detail items stay
  simple: `Item(x => x.Customer.Name)` throws, and so does a raw detail spec naming a nested path.
- Derived classes keep XAF's default layout unless they declare their own. A base class's spec is
  not inherited.

## ListView surface

- `ListViewColumnsBuilder<T>.Create()`, then `.Column(x => x.M, width: null,
  sort: ColumnSortOrder.None, caption: null, sortIndex: null)`, `.Hide(x => x.M)`, `.Lookup(l => ...)`, `.Build()`.
- Column order is call order. Sorted columns get sort priority in call order, unless they set
  `sortIndex` (0 first), which decides independently of column order: sort by Customer, then
  OrderDate, while OrderDate is shown first. Set it on every sorted column of a list or on none; it
  needs a sort order, must not be negative, and must be unique.
- `.Band("Identity", b => b.Column(x => x.Number).Column(x => x.Customer), caption: "Order")` puts a
  band header over the columns declared inside it; they keep their place in the column order, and a
  null caption shows the id. One level only (no `Band` inside `Band`, which XAF Blazor would not
  render), a band's columns must be adjacent, and `.Lookup(...)` takes no bands.
- `Hide` keeps the column in the column chooser, and leaving a member out does the same, as long as
  XAF generated a column for it. Lookup views generate almost none, so in a `.Lookup(...)` list a
  member you neither list nor hide has no column at all.
- A column XAF sorted by default is unsorted unless the spec sorts it, and a hidden column does not
  keep a sort order.
- `.Lookup(...)` describes `{Type}_LookupListView`. Without it XAF's default lookup stays.
- The same spec shapes every nested ListView of `T`, the grid of a `T` collection inside another
  class (`Order_Lines_ListView` for `Order.Lines`). A column for the reference back to the owner is
  left out there, as XAF does, even when the spec lists it.

## Rules that throw at Build()

A member placed twice, a member both placed and hidden, a group id used twice in the view, a group
and an item with the same id under one parent, a column listed twice, a column both listed and
hidden, `Lookup` inside `Lookup`, a non-simple member lambda.

The same rules, plus a blank id or member name, hold for a spec that never went through a builder
(a record constructed by hand, reshaped with `with`, or loaded from JSON): `LayoutRegistry.Register`
and the module check it before applying it, and `LayoutSpecChecks.Validate(spec)` runs them on demand.
`LayoutSpecChecks.CheckAgainstView(spec, viewId, itemIds, visibleEditorIds)` runs XLB001 and XLB002
against a view's item ids without an XAF application; the module runs it before it changes a layout,
so a rejected spec leaves XAF's generated layout untouched.

## Startup diagnostics

The module generates every view that has a spec when the application model is built, so these
surface at startup. What they do is the host's choice, through
`XafLayoutBuilderModule.FailFastOnLayoutErrors`, set from configuration:

- **Off, the default:** each broken view is written to XAF's `eXpressAppFramework.log` and keeps
  XAF's own layout or columns; the application starts.
- **On:** the application stops at startup. In XAF Blazor the host exits before it listens; read the
  console. Every view with a spec is checked, and all broken views are reported together in one
  exception.

Turn it on in development and CI so a broken layout cannot slip through unnoticed. Off, a broken
layout looks like a view that silently ignores its `.Layout.cs`: check the log.

- XLB001: a placed member has no view item: it is `[Browsable(false)]`, hidden with `[HideInUI]`, or
  the reference back to its owner. `[VisibleInDetailView(false)]` does not remove the view item; such
  a member needs no placing but can still be placed.
- XLB002: a visible member is neither placed nor hidden.
- XLB003: a column names a collection or something that is not a member.
- XLB004: a type has a spec but no default view, or a declared view could not be added.
- XLB005: a view declared with `AddDetailView`/`AddListView` has a blank id, one another view already has, or one
  also declared for another class or kind of view. Declaring the same view again replaces it.

## When you change a business class

- **Added a property?** Place it with `.Item(...)` or `.Hide(...)` in `BuildDetailViewLayout`, or
  the layout is rejected (the app will not start with fail-fast on), unless that class opted into
  `.Unplaced(...)`. Add a `.Column(...)` only
  if the list should show it.
- **Renamed or removed one?** The lambda stops compiling; fix it where the compiler points.
- **Layout looks unchanged after a restart?** An administrator or user customised that view and
  their differences win. The layout editor's context menu has Reset Layout; resetting their
  differences shows the builder output again.

## Export Layout To Code

The running app is the visual designer. On any DetailView or ListView the Tools tab shows
**Export Layout To Code** for administrators, when a debugger is attached or the host sets
`XafLayoutBuilderModule.EnableExport` (the sample reads `XafLayoutBuilder:EnableExport` from
appsettings.Development.json; a host without a security system shows it to everyone). It prints the
type's DetailView, ListView and lookup with every layer applied, as the `{Type}.Layout.cs` class
above, namespace included. The view you run it from is the one exported, and the comment at the top
names the view ids it read. It writes nothing to disk.

In a Blazor host that also references `XafLayoutBuilder.Blazor`, the same Tools tab has two more
actions that skip the popup: **Copy Layout To Clipboard**, and **Download Layout File**, which
saves `{Type}.Layout.cs` straight to the downloads folder. All three print the identical text.

**Export Layout To JSON** shows the same export as one `LayoutSpecs` JSON document (`detail` and
`columns`), and **Download Layout JSON** saves it as `{Type}.layout.json`. That is the form
`LayoutRegistry.RegisterJson<T>(() => File.ReadAllText(path))` reads and a generator can emit. JSON
has no comments, so the view ids and skipped items the C# export lists are not in it. XAF
Blazor renders only OK and Cancel inside a popup for a non-persistent object, which is why the
buttons are not in the popup itself.

- Prints groups, tabs, items, captions that differ from XAF's default, flow, collapsible, explicit
  relative sizes and images. Layout items that are not property editors are listed in a comment.
- Lists every unplaced visible member as `.Hide(...)`. For columns it prints `.Hide(...)` only for
  one the spec hid, or one a later layer hid or added, never the key; a column the spec never
  mentioned stays out. A hidden column's sort order is not carried over.
- Skips what the builder cannot express, such as a layout item bound to a nested path or a column
  path that casts to a descendant class, and names it in the leading comment instead of printing
  code that would not compile or would throw. A column over a reference's member is printed as
  `.Column(x => x.Customer.City)`.
- Prints `.Unplaced(...)` again for a class that opted in, instead of the members its catch-all
  group happens to hold at that moment, so the exported file keeps behaving the same way.
- Prints `sortIndex:` on the sorted columns only when their sort priority differs from their column
  order, so a list that sorts in column order exports without it. Grouping is not exported: a column
  grouped in the grid keeps its sort order and comes first in sort priority, the way the grid sorts.
- Prints `.Band(...)` around each band's columns, with `caption:` only when the caption is not the
  band id.
