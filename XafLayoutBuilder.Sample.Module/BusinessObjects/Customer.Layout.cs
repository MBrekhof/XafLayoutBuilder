using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// This class shows two things the Order sample does not: a column caption, which is the exporter's localizable-value
// trap, and the opt-in relaxation of the strict placement rule. City is neither placed nor hidden, so instead of
// failing startup with XLB002 it lands in the "Other" group at the end of the form.
public partial class Customer : ISupportViewLayoutCustomization {
    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<Customer>.Create()
            .Group("Identification", g => g
                .Caption("Identification")
                .Item(x => x.Name))
            .Unplaced(UnplacedMembers.AppendToGroup("Other"))
            .Build();

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<Customer>.Create()
            .Column(x => x.Name, caption: "Customer name")
            .Column(x => x.City)
            .Build();
}
