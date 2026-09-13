using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DxSort = DevExpress.Data.ColumnSortOrder;

namespace XafLayoutBuilder.Tests;

// MODEL-001: ListViewColumnsUpdater against a real Application Model (ApplicationModelFixture).
[Collection(ApplicationModelCollection.Name)]
public class ListViewColumnsUpdaterTests(ApplicationModelFixture fixture) {
    static IEnumerable<IModelColumn> Shown(IModelListView view) => view.Columns.Where(c => c.Index >= 0).OrderBy(c => c.Index);

    [Fact]
    public void ListedColumns_AreShownInSpecOrder_WithWidthAndSort() {
        var view = fixture.Class<ModelTestOrder>().DefaultListView;
        Assert.Equal(["Number", "Customer", "OrderDate", "Customer.City"], Shown(view).Select(c => c.PropertyName));
        Assert.Equal(90, view.Columns["Number"].Width);
        Assert.Equal(DxSort.Descending, view.Columns["OrderDate"].SortOrder);
        Assert.Equal(0, view.Columns["OrderDate"].SortIndex);
        Assert.All(Shown(view).Where(c => c.Id != "OrderDate"), c => Assert.Equal(DxSort.None, c.SortOrder));
    }

    // FREEZE-001 depends on this: an explicit Index written by the generated layer would keep a column shown even when an
    // administrator froze the column set without it.
    [Fact]
    public void ListedColumns_AreOrderedThroughGeneratedIndex_NotAnExplicitIndex() {
        var view = fixture.Class<ModelTestOrder>().DefaultListView;
        Assert.All(Shown(view), c => Assert.False(((ModelNode)c).HasValue(ModelValueNames.Index), $"{c.Id} has an explicit Index"));
    }

    [Fact]
    public void HiddenMember_KeepsItsColumnForTheChooser_ButIsNotShown() {
        var column = fixture.Class<ModelTestOrder>().DefaultListView.Columns["SyncToken"];
        Assert.NotNull(column);
        Assert.True(column.Index is null or < 0);
    }

    [Fact]
    public void NestedColumn_IsAddedWithTheDottedPath_AndResolvesItsMember() {
        var column = fixture.Class<ModelTestOrder>().DefaultListView.Columns["Customer.City"];
        Assert.NotNull(column);
        Assert.Equal("Customer.City", column.PropertyName);
        Assert.Equal("City", column.ModelMember?.Name);
    }

    [Fact]
    public void Lookup_GetsItsOwnColumnSet() =>
        Assert.Equal(["Number", "Customer"], Shown(fixture.Class<ModelTestOrder>().DefaultLookupListView).Select(c => c.PropertyName));
}
