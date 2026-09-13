using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;
using XafLayoutBuilder.Sample.Module.BusinessObjects;

namespace XafLayoutBuilder.Sample.Module;

/// <summary>
/// VIEW-001: a second DetailView and ListView of Order declared in code, next to the class's default ones. The host
/// registers them before the application model is built; the E2E gate opens both by id.
/// </summary>
public static class SampleViews {
    public const string CompactDetailViewId = "Order_Compact_DetailView";
    public const string CompactListViewId = "Order_Compact_ListView";

    public static void Register() {
        LayoutRegistry.AddDetailView<Order>(CompactDetailViewId, () =>
            LayoutBuilder<Order>.Create()
                .Group("Compact", g => g.Caption("Compact order").Item(x => x.Number).Item(x => x.OrderDate))
                .Hide(x => x.Customer).Hide(x => x.Notes).Hide(x => x.SyncToken).Hide(x => x.Lines).Hide(x => x.Attachments)
                .Build());
        LayoutRegistry.AddListView<Order>(CompactListViewId, () =>
            ListViewColumnsBuilder<Order>.Create()
                .Column(x => x.Number)
                .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
                .Build());
    }
}
