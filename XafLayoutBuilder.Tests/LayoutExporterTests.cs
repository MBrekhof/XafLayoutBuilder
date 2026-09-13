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
        // Hidden and unmentioned columns look the same in the model, so the export hides every column it does not list.
        Assert.Subset(exported.HiddenMembers.ToHashSet(), source.HiddenMembers.ToHashSet());
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
