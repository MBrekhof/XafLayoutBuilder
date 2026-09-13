using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// VIEW-001: a columns spec for an element type also shapes its nested ListViews, here Order_Lines_ListView on Order's
// Lines tab. The order is the reverse of XAF's own, so the gate can tell the two apart. Order is listed on purpose: the
// nested view keeps the back-reference hidden, as XAF generates it.
public partial class OrderLine : ISupportViewLayoutCustomization {
    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<OrderLine>.Create()
            .Column(x => x.UnitPrice)
            .Column(x => x.Quantity)
            .Column(x => x.Product)
            .Column(x => x.Order)
            .Build();
}
