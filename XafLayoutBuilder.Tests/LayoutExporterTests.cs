using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Tests;

// MODEL-001: the start document's section 6 round trip as a unit test. Exporting the untouched model gives back the spec
// the builder produced (ApplicationModelFixture).
[Collection(ApplicationModelCollection.Name)]
public class LayoutExporterTests(ApplicationModelFixture fixture) {
    [Fact]
    public void ExportDetail_OfTheUntouchedModel_PrintsTheSourceSpec() {
        var (exported, skipped) = LayoutExporter.ExportDetail(fixture.Class<ModelTestOrder>().DefaultDetailView);
        Assert.Empty(skipped);
        Assert.Equal(CSharpLayoutPrinter.PrintDetail(ModelTestOrder.BuildDetailViewLayout()!, nameof(ModelTestOrder)),
            CSharpLayoutPrinter.PrintDetail(exported, nameof(ModelTestOrder)));
    }

    [Fact]
    public void ExportDetail_KeepsTheUnplacedOptIn_InsteadOfFreezingTheLeftovers() {
        var (exported, _) = LayoutExporter.ExportDetail(fixture.Class<ModelTestContact>().DefaultDetailView);
        Assert.Equal(CSharpLayoutPrinter.PrintDetail(ModelTestContact.BuildDetailViewLayout()!, nameof(ModelTestContact)),
            CSharpLayoutPrinter.PrintDetail(exported, nameof(ModelTestContact)));
    }

    [Fact]
    public void ExportColumns_OfTheUntouchedModel_ListsTheSourceColumnsAndLookup() {
        var modelClass = fixture.Class<ModelTestOrder>();
        var (exported, skipped) = LayoutExporter.ExportColumns(modelClass.DefaultListView, modelClass.DefaultLookupListView);
        var source = ModelTestOrder.BuildListViewColumns()!;
        Assert.Empty(skipped);
        Assert.Equal(source.Columns, exported.Columns);
        Assert.Equal(source.Lookup!.Columns, exported.Lookup!.Columns);
        // EXPORT-001: only what the spec hid, not every column it left unmentioned.
        Assert.Equal(source.HiddenMembers, exported.HiddenMembers);
        Assert.Equal(source.Lookup.HiddenMembers, exported.Lookup.HiddenMembers);
    }

    // EXPORT-001: a later layer hides Subject, which the builder showed, and adds a hidden Customer.City column, which no
    // generator made; both are exported as hidden (without the second, re-applying the export would lose that column from
    // the chooser, Codex review). Notes and Customer, generated but never mentioned by the spec, stay out of the export.
    [Fact]
    public void ExportColumns_HidesWhatALaterLayerHidOrAdded_ButNotWhatTheSpecNeverMentioned() {
        var view = fixture.Class<ModelTestTicket>().DefaultListView;
        view.Columns[nameof(ModelTestTicket.Subject)].Index = -1;
        var added = view.Columns.AddNode<DevExpress.ExpressApp.Model.IModelColumn>("Customer.City");
        added.PropertyName = "Customer.City";
        added.Index = -1;

        var (exported, _) = LayoutExporter.ExportColumns(view, null);

        Assert.Equal([new ColumnSpec(nameof(ModelTestTicket.Number))], exported.Columns);
        Assert.Equal(["Customer.City", nameof(ModelTestTicket.Subject)], exported.HiddenMembers.Order());
    }

    // BAND-001: bands export with their captions, and each column with its band.
    [Fact]
    public void ExportColumns_KeepsBandsAndTheirColumns() {
        var (exported, _) = LayoutExporter.ExportColumns(fixture.Class<ModelTestBanded>().DefaultListView, null);
        var source = ModelTestBanded.BuildListViewColumns()!;
        Assert.Equal(source.Columns, exported.Columns);
        Assert.Equal(source.Bands!, exported.Bands!);
    }

    // BAND-001, Codex review: once the Blazor grid saves a banded layout it numbers columns within each band
    // (SynchronizeVisibleIndexesToBands), so the export follows the display order instead of sorting all columns by Index.
    [Fact]
    public void ExportColumns_FollowsTheDisplayOrder_WhenColumnsAreNumberedWithinTheirBands() {
        var view = fixture.Class<ModelTestTwoBands>().DefaultListView;
        _ = view.Columns.Count;
        view.BandsLayout["A"]!.Index = 0;
        view.BandsLayout["B"]!.Index = 1;
        foreach (var (member, index) in new[] { ("A1", 0), ("A2", 1), ("B1", 0), ("B2", 1) })
            view.Columns[member]!.Index = index;

        var (exported, _) = LayoutExporter.ExportColumns(view, null);

        LayoutSpecChecks.Validate(exported);
        Assert.Equal(ModelTestTwoBands.BuildListViewColumns()!.Columns, exported.Columns);
    }

    // BAND-001, Codex re-review: XAF Blazor renders one band level, so a band nested in another (possible from XAFML) exports
    // its columns under the outermost band, with a note, instead of an export that splits the outer band in two.
    [Fact]
    public void ExportColumns_FlattensANestedBandIntoItsOuterBand_AndSaysSo() {
        var view = fixture.Class<ModelTestNestedBands>().DefaultListView;
        _ = view.Columns.Count;
        var outer = view.BandsLayout["P"]!;
        var inner = view.BandsLayout.AddNode<IModelBand>("C");
        inner.OwnerBand = outer;
        inner.Index = 1;
        ((IModelBandedColumn)view.Columns["C1"]!).OwnerBand = inner;
        view.Columns["C1"]!.Index = 0;
        view.Columns["P1"]!.Index = 0;
        view.Columns["P2"]!.Index = 2;

        var (exported, skipped) = LayoutExporter.ExportColumns(view, null);

        LayoutSpecChecks.Validate(exported);
        Assert.Equal(["P1", "C1", "P2"], exported.Columns.Select(c => c.Member));
        Assert.All(exported.Columns, c => Assert.Equal("P", c.Band));
        Assert.Contains(skipped, s => s.Contains("nested band"));
    }

    // BAND-001, Codex re-review: siblings on one index come out in the order XAF gives them (ModelBandedLayoutItemComparer).
    [Fact]
    public void ExportColumns_OrdersSiblingsOnOneIndexTheWayXafDoes() {
        var view = fixture.Class<ModelTestTiedBands>().DefaultListView;
        _ = view.Columns.Count;
        view.Columns["Alpha"]!.Index = 0;
        view.BandsLayout["Zeta"]!.Index = 0;

        var (exported, _) = LayoutExporter.ExportColumns(view, null);

        Assert.Equal(["Alpha", "Beta"], exported.Columns.Select(c => c.Member));
    }

    // BAND-001, Codex review: Lookup() takes no bands, so a lookup view with bands switched on exports its columns without them.
    [Fact]
    public void ExportColumns_DropsTheLookupsBands_AndSaysSo() {
        var modelClass = fixture.Class<ModelTestBanded>();
        var lookup = modelClass.DefaultLookupListView;
        var column = lookup.Columns.First(c => c.Index is >= 0);
        lookup.BandsLayout.Enable = true;
        var band = lookup.BandsLayout.AddNode<IModelBand>("LookupBand");
        band.Index = 0;
        ((IModelBandedColumn)column).OwnerBand = band;

        var (exported, skipped) = LayoutExporter.ExportColumns(modelClass.DefaultListView, lookup);

        LayoutSpecChecks.Validate(exported);
        Assert.All(exported.Lookup!.Columns, c => Assert.Null(c.Band));
        Assert.Contains(skipped, s => s.Contains("Lookup() takes no bands"));
    }

    // SORT-001: when sort priority differs from column order the export keeps the explicit indexes; the Order round trip
    // above covers the default, where it follows column order and nothing is printed.
    [Fact]
    public void ExportColumns_KeepsExplicitSortIndexes_WhenPriorityDiffersFromColumnOrder() {
        var (exported, skipped) = LayoutExporter.ExportColumns(fixture.Class<ModelTestShipment>().DefaultListView, null);
        Assert.Empty(skipped);
        Assert.Equal(ModelTestShipment.BuildListViewColumns()!.Columns, exported.Columns);
    }

    // SORT-001, Codex review: the Blazor grid stores a grouped column with its SortOrder but SortIndex -1, and sorts it
    // before every other column (docs/api-notes.md). The export ranks it the same way instead of printing sortIndex: -1.
    [Fact]
    public void ExportColumns_RanksAGroupedColumnFirst_InsteadOfExportingItsSortIndex() {
        var view = fixture.Class<ModelTestParcel>().DefaultListView;
        var shipDate = view.Columns[nameof(ModelTestParcel.ShipDate)];
        shipDate.GroupIndex = 0;
        shipDate.SortIndex = -1;

        var (exported, _) = LayoutExporter.ExportColumns(view, null);

        LayoutSpecChecks.Validate(exported);
        Assert.Equal(
            [new ColumnSpec(nameof(ModelTestParcel.Number)),
             new ColumnSpec(nameof(ModelTestParcel.Customer), SortOrder: ColumnSortOrder.Ascending, SortIndex: 1),
             new ColumnSpec(nameof(ModelTestParcel.ShipDate), SortOrder: ColumnSortOrder.Descending, SortIndex: 0)],
            exported.Columns);
    }
}
