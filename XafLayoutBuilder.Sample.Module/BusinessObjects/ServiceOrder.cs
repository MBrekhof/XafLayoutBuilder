using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// Inheritance pair for phase-2 layout composition; in the POC it just inherits Order's model.
[DefaultClassOptions]
public class ServiceOrder : Order {
    public virtual DateTime? ServiceDate { get; set; }
    public virtual string? Technician { get; set; }
}
