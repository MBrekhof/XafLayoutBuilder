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
