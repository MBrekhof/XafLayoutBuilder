using System.ComponentModel;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

[DefaultProperty(nameof(FileName))]
public class OrderAttachment : BaseObject {
    public virtual Order? Order { get; set; }
    public virtual string FileName { get; set; } = "";
}
