using System.Collections.ObjectModel;
using System.ComponentModel;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafLayoutBuilder.Sample.Module.BusinessObjects;

// The layout subject. Deliberately has NO Model.DesignedDiffs.xafml entry: the builder
// (session 3+) is the only source of its DetailView layout and ListView columns.
[DefaultClassOptions]
[DefaultProperty(nameof(Number))]
public partial class Order : BaseObject {
    public virtual string Number { get; set; } = "";
    public virtual Customer? Customer { get; set; }
    public virtual DateTime OrderDate { get; set; }
    public virtual string? Notes { get; set; }
    public virtual string? SyncToken { get; set; }

    [Aggregated]
    public virtual IList<OrderLine> Lines { get; set; } = new ObservableCollection<OrderLine>();

    [Aggregated]
    public virtual IList<OrderAttachment> Attachments { get; set; } = new ObservableCollection<OrderAttachment>();
}
