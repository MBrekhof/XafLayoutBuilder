using System.ComponentModel;
using DevExpress.ExpressApp.DC;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

// Business classes for the in-process Application Model (ApplicationModelFixture). Top level and public on purpose: XAF
// gives a type a BOModel class and views only when Type.IsPublic, which is false for every nested type. Non-persistent
// [DomainComponent] classes need no EF Core types info source. This file imports only XafLayoutBuilder.Core, so
// FlowDirection and ColumnSortOrder are not ambiguous with DevExpress's own.

[DomainComponent]
public class ModelTestCustomer {
    public string? Name { get; set; }
    public string? City { get; set; }
}

// VIEW-001: its columns spec reverses XAF's order, so ModelTestOrder_Lines_ListView shows whether the spec reached it.
[DomainComponent]
public class ModelTestLine : ISupportViewLayoutCustomization {
    public string? Product { get; set; }
    public int Quantity { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestLine>.Create()
            .Column(x => x.Quantity)
            .Column(x => x.Product)
            .Build();
}

[DomainComponent]
public class ModelTestOrder : ISupportViewLayoutCustomization {
    public string? Number { get; set; }
    public ModelTestCustomer? Customer { get; set; }
    public DateTime OrderDate { get; set; }
    public string? Notes { get; set; }
    public string? SyncToken { get; set; }
    public IList<ModelTestLine> Lines { get; } = new List<ModelTestLine>();

    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ModelTestOrder>.Create()
            .Group("Header", g => g
                .Caption("Order")
                .Flow(FlowDirection.Horizontal)
                .Item(x => x.Number)
                .Item(x => x.Customer)
                .Item(x => x.OrderDate))
            .Group("Details", g => g
                .Collapsible()
                .Item(x => x.Notes, relativeSize: 100))
            .Tabs("Tabs", t => t
                .TabFor(x => x.Lines))
            .Hide(x => x.SyncToken)
            .Build();

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestOrder>.Create()
            .Column(x => x.Number, width: 90)
            .Column(x => x.Customer)
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
            .Column(x => x.Customer!.City)
            .Hide(x => x.SyncToken)
            .Lookup(l => l
                .Column(x => x.Number)
                .Column(x => x.Customer))
            .Build();
}

// BAND-001: Number and Customer in the band "Identity", captioned "Order"; OrderDate outside any band.
[DomainComponent]
public class ModelTestBanded : ISupportViewLayoutCustomization {
    public string? Number { get; set; }
    public string? Customer { get; set; }
    public DateTime OrderDate { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestBanded>.Create()
            .Band("Identity", b => b.Column(x => x.Number).Column(x => x.Customer), caption: "Order")
            .Column(x => x.OrderDate)
            .Build();
}

// BAND-001, Codex review: two bands of two columns, which LayoutExporterTests renumbers the way the Blazor grid saves them.
[DomainComponent]
public class ModelTestTwoBands : ISupportViewLayoutCustomization {
    public string? A1 { get; set; }
    public string? A2 { get; set; }
    public string? B1 { get; set; }
    public string? B2 { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestTwoBands>.Create()
            .Band("A", b => b.Column(x => x.A1).Column(x => x.A2))
            .Band("B", b => b.Column(x => x.B1).Column(x => x.B2))
            .Build();
}

// BAND-001, Codex re-review: P1, C1 and P2 in band "P"; LayoutExporterTests nests a band "C" holding C1 inside it.
[DomainComponent]
public class ModelTestNestedBands : ISupportViewLayoutCustomization {
    public string? P1 { get; set; }
    public string? C1 { get; set; }
    public string? P2 { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestNestedBands>.Create()
            .Band("P", b => b.Column(x => x.P1).Column(x => x.C1).Column(x => x.P2))
            .Build();
}

// BAND-001, Codex re-review: an unbanded Alpha column and a Zeta band holding Beta, which LayoutExporterTests puts on one index.
[DomainComponent]
public class ModelTestTiedBands : ISupportViewLayoutCustomization {
    public string? Alpha { get; set; }
    public string? Beta { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestTiedBands>.Create()
            .Column(x => x.Alpha)
            .Band("Zeta", b => b.Column(x => x.Beta))
            .Build();
}

// VIEW-001: views declared in code for ModelTestOrder, registered by ApplicationModelFixture before it builds the model.
public static class ModelTestDeclaredViews {
    public const string DetailViewId = "ModelTestOrder_Compact_DetailView";
    public const string ListViewId = "ModelTestOrder_Compact_ListView";

    public static void Register() {
        XafLayoutBuilder.Module.LayoutRegistry.AddDetailView<ModelTestOrder>(DetailViewId, () =>
            LayoutBuilder<ModelTestOrder>.Create()
                .Group("Compact", g => g.Item(x => x.Number).Item(x => x.OrderDate))
                .Hide(x => x.Customer).Hide(x => x.Notes).Hide(x => x.SyncToken).Hide(x => x.Lines)
                .Build());
        XafLayoutBuilder.Module.LayoutRegistry.AddListView<ModelTestOrder>(ListViewId, () =>
            ListViewColumnsBuilder<ModelTestOrder>.Create()
                .Column(x => x.OrderDate)
                .Column(x => x.Number)
                .Build());
    }
}

// HIER-001: a derived class whose layout and columns start from ModelTestOrder's and add its own member.
[DomainComponent]
public class ModelTestServiceOrder : ModelTestOrder, ISupportViewLayoutCustomization {
    public string? Technician { get; set; }

    public static new DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ModelTestServiceOrder>.Extend<ModelTestOrder>()
            .InGroup("Header", g => g.Item(x => x.Technician))
            .Build();

    public static new ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestServiceOrder>.Extend<ModelTestOrder>()
            .Column(x => x.Technician)
            .Build();
}

// RECHECK-001, Codex review: a valid layout whose Notes editor StartupCheckTests removes afterwards, as a later layer could.
[DomainComponent]
public class ModelTestLaterRemoval : ISupportViewLayoutCustomization {
    public string? Name { get; set; }
    public string? Notes { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ModelTestLaterRemoval>.Create().Group("Info", g => g.Item(x => x.Name).Item(x => x.Notes)).Build();

    public static ListColumnsSpec? BuildListViewColumns() => null;
}

// Places one member and lets the catch-all group collect the rest.
[DomainComponent]
public class ModelTestContact : ISupportViewLayoutCustomization {
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ModelTestContact>.Create()
            .Group("Identification", g => g.Item(x => x.Name))
            .Unplaced(UnplacedMembers.AppendToGroup("Other"))
            .Build();

    public static ListColumnsSpec? BuildListViewColumns() => null;
}

// Both place a member XAF generates no editor for (XLB001). Two identical types because a view's layout is generated
// once, on first read: one is read with FailFastOnLayoutErrors on, the other with it off.
[DomainComponent]
public class ModelTestStrictBroken : ISupportViewLayoutCustomization {
    public string? Name { get; set; }
    [Browsable(false)] public string? InternalCode { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ModelTestStrictBroken>.Create().Group("Broken", g => g.Item(x => x.Name).Item(x => x.InternalCode)).Build();

    public static ListColumnsSpec? BuildListViewColumns() => null;
}

[DomainComponent]
public class ModelTestDegradedBroken : ISupportViewLayoutCustomization {
    public string? Name { get; set; }
    [Browsable(false)] public string? InternalCode { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() =>
        LayoutBuilder<ModelTestDegradedBroken>.Create().Group("Broken", g => g.Item(x => x.Name).Item(x => x.InternalCode)).Build();

    public static ListColumnsSpec? BuildListViewColumns() => null;
}

// MODELEDITOR-006, Codex review 2: plain classes, no [DomainComponent], standing in for a reference to an EF Core entity,
// which is persistent but no domain component. Only ModelEditorSpecialEditorsTests reads them; they get no views.
public class ModelTestPlainPart {
    public string? Code { get; set; }
}

public class ModelTestPlainOwner {
    public string? Name { get; set; }
    public ModelTestPlainPart? Part { get; set; }
}

// SORT-001: sorted by Customer, then ShipDate, while ShipDate is shown first.
[DomainComponent]
public class ModelTestShipment : ISupportViewLayoutCustomization {
    public string? Number { get; set; }
    public ModelTestCustomer? Customer { get; set; }
    public DateTime ShipDate { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestShipment>.Create()
            .Column(x => x.ShipDate, sort: ColumnSortOrder.Descending, sortIndex: 1)
            .Column(x => x.Customer, sort: ColumnSortOrder.Ascending, sortIndex: 0)
            .Column(x => x.Number)
            .Build();
}

// EXPORT-001: LayoutExporterTests hides Subject and adds a hidden Customer.City the way a later layer does; Notes and
// Customer are never mentioned.
[DomainComponent]
public class ModelTestTicket : ISupportViewLayoutCustomization {
    public string? Number { get; set; }
    public string? Subject { get; set; }
    public string? Notes { get; set; }
    public ModelTestCustomer? Customer { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestTicket>.Create()
            .Column(x => x.Number)
            .Column(x => x.Subject)
            .Build();
}

// SORT-001: sorted in column order; LayoutExporterTests groups ShipDate the way the Blazor grid stores it.
[DomainComponent]
public class ModelTestParcel : ISupportViewLayoutCustomization {
    public string? Number { get; set; }
    public ModelTestCustomer? Customer { get; set; }
    public DateTime ShipDate { get; set; }

    public static DetailLayoutSpec? BuildDetailViewLayout() => null;

    public static ListColumnsSpec? BuildListViewColumns() =>
        ListViewColumnsBuilder<ModelTestParcel>.Create()
            .Column(x => x.Number)
            .Column(x => x.Customer, sort: ColumnSortOrder.Ascending)
            .Column(x => x.ShipDate, sort: ColumnSortOrder.Descending)
            .Build();
}
