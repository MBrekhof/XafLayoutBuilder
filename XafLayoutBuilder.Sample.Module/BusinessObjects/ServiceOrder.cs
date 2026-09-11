using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// Inheritance pair for phase-2 layout composition; in the POC it just inherits Order's model.
[DefaultClassOptions]
public class ServiceOrder : Order {
    public virtual DateTime? ServiceDate { get; set; }
    public virtual string? Technician { get; set; }
    /// <summary>The sales order this service call belongs to. Its lookup editor is how E2E 3 reaches Order_LookupListView.</summary>
    public virtual Order? OriginalOrder { get; set; }
}
