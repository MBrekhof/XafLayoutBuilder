using System.ComponentModel;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Customer : BaseObject {
    public virtual string Name { get; set; } = "";
    public virtual string? City { get; set; }
}
