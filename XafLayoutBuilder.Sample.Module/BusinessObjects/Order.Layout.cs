using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// The section 4 example from the start document, verbatim. This file is Order's only layout source.
public partial class Order : ISupportViewLayoutCustomization, ISupportAppearanceRules {
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

    // APPEAR-001: applied by the XafLayoutBuilder.Appearance add-on the host registers.
    public static AppearanceSpec? BuildAppearanceRules() =>
        AppearanceBuilder<Order>.Create()
            .Rule("GlobexOrder", r => r
                .When("[Customer.Name] = 'Globex'")
                .On(x => x.Number)
                .FontColor("DarkRed")
                .FontStyle(AppearanceFontStyle.Bold)
                .InListView())
            .Rule("HeaderCaption", r => r
                .OnLayout("Header")
                .FontColor("DarkBlue")
                .InDetailView())
            .Build();
}
