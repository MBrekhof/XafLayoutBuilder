using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// Inheritance pair: ServiceOrder.Layout.cs extends Order's layout and columns (HIER-001).
[DefaultClassOptions]
public partial class ServiceOrder : Order {
    public virtual DateTime? ServiceDate { get; set; }
    public virtual string? Technician { get; set; }
    /// <summary>The sales order this service call belongs to. Its lookup editor is how E2E 3 reaches Order_LookupListView.</summary>
    public virtual Order? OriginalOrder { get; set; }
}
