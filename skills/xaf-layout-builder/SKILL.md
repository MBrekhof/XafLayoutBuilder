---
name: xaf-layout-builder
description: Declare a DevExpress XAF class's DetailView layout and ListView columns in fluent C# with XafLayoutBuilder instead of Model.xafml. Use when adding or changing the layout of an XAF business class in a solution that references XafLayoutBuilder.Module.
---

# XafLayoutBuilder

Typed, compile-checked layout for XAF. The builder output becomes the generated (zero) layer of
the Application Model; module XAFML, admin and user differences still apply on top. Changes show
after an application restart. Draft as of session 4; finalised in session 7.

## Opt in

Implement `ISupportViewLayoutCustomization` on the business class (both static members are
required; return `null` from one to leave that view to XAF). Put it in a `*.Layout.cs` partial.

```csharp
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

For a type you do not own: `LayoutRegistry.Register<ReportDataV2>(detail, columns)` in your
module's constructor. Registry entries win over the interface. Registration throws if the spec
names a member the type does not have.

## DetailView surface

- `LayoutBuilder<T>.Create()` then `.Group(id, g => ...)`, `.Tabs(id, t => ...)`, `.Hide(x => x.M)`, `.Build()`.
- Group: `.Caption("...")`, `.Flow(FlowDirection.Horizontal|Vertical)`, `.Collapsible()`,
  `.RelativeSize(percent)`, `.Image("ImageName")`, `.Item(x => x.M, relativeSize: null)`,
  and nested `.Group(...)` / `.Tabs(...)`.
- Tabs: `.TabFor(x => x.Collection, imageName: null, caption: null)` makes one tab holding that
  member; `.Tab(id, g => ...)` makes a tab with arbitrary content.
- `Collapsible()` always shows the group caption (XAF Blazor puts the toggle in the caption
  header). A group without an explicit caption and with one item shows that item's caption.
- Member lambdas must be simple: `x => x.Customer`. `x => x.Customer.Name` throws.
- Every visible member must be placed or hidden. An unplaced member fails at startup with
  XLB002 naming it, so a new property cannot silently vanish from the form.
- `Hide` removes the item from the DetailView entirely.
- Derived classes keep XAF's default layout unless they declare their own; a base class's spec is
  not inherited.

## ListView surface

- `ListViewColumnsBuilder<T>.Create()` then `.Column(x => x.M, width: null, sort:
  ColumnSortOrder.None, caption: null)`, `.Hide(x => x.M)`, `.Lookup(l => ...)`, `.Build()`.
- Column order is call order; sorted columns get sort priority in call order.
- `Hide` keeps the column available in the column chooser (index -1). Members not mentioned at all
  are treated the same way, so nothing is lost. Any column XAF sorted by default (the display
  property) is unsorted unless the spec sorts it.
- `.Lookup(...)` describes `{Type}_LookupListView`; without it XAF's default lookup stays.
  Lookup cannot nest.

## Rules that throw at Build()

A member placed twice, a member both placed and hidden, a group id used twice in the view, a
group and an item with the same id under one parent, a column listed twice, a column both listed
and hidden, `Lookup` inside `Lookup`, a non-simple member lambda.

## Startup diagnostics

- XLB001: a placed member has no view item (for example `[Browsable(false)]`).
- XLB002: a visible member is neither placed nor hidden.
- XLB003: a column names a collection or a non-member.

Until session 5 lands, these fire when the view is first opened rather than at application start.
