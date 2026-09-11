using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

[DefaultProperty(nameof(Product))]
public class OrderLine : BaseObject {
    public virtual Order? Order { get; set; }
    public virtual string Product { get; set; } = "";
    public virtual int Quantity { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public virtual decimal UnitPrice { get; set; }
}
