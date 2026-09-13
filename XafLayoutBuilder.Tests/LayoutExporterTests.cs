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
}
