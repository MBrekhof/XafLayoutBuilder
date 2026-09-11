using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// ListView only. The column caption is here so the E2E round trip covers caption export; the DetailView stays XAF's.
public partial class Customer : ISupportViewLayoutCustomization {
    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<Customer>.Create()
            .Column(x => x.Name, caption: "Customer name")
            .Column(x => x.City)
            .Build();
}
