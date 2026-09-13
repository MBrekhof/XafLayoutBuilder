using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// HIER-001: ServiceOrder starts from Order's layout and columns and adds its own members. It re-implements the interface,
// so XAF finds ServiceOrder's builders rather than the ones it inherits from Order.
public partial class ServiceOrder : ISupportViewLayoutCustomization {
    public static new DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ServiceOrder>.Extend<Order>()
            .InGroup("Header", g => g.Item(x => x.OriginalOrder))
            .Group("Service", g => g
                .Caption("Service")
                .Flow(FlowDirection.Horizontal)
                .Item(x => x.ServiceDate)
                .Item(x => x.Technician))
            .Build();

    public static new ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ServiceOrder>.Extend<Order>()
            .Column(x => x.Technician)
            .Build();
}
