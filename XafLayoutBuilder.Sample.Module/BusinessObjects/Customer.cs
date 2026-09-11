using System.ComponentModel;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public partial class Customer : BaseObject {
    public virtual string Name { get; set; } = "";
    public virtual string? City { get; set; }
    /// <summary>Not a view item. BrokenLayouts places it on purpose to trigger XLB001 at startup.</summary>
    [Browsable(false)]
    public virtual string? InternalCode { get; set; }
}
