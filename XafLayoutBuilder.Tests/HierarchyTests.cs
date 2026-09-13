using DevExpress.ExpressApp.Model;

namespace XafLayoutBuilder.Tests;

// HIER-001: a derived class's layout and columns start from its base's (LayoutBuilder<T>.Extend<TBase>()) and are applied
// by the updaters like any other spec (ApplicationModelFixture).
[Collection(ApplicationModelCollection.Name)]
public class HierarchyTests(ApplicationModelFixture fixture) {
    [Fact]
    public void ADerivedClass_GetsItsBaseLayout_WithItsOwnAdditions() =>
        Assert.Equal(["Main", "Header", "Number", "Customer", "OrderDate", "Technician", "Details", "Notes", "Tabs", "Lines", "Lines"],
            ApplicationModelFixture.LayoutIds(fixture.Class<ModelTestServiceOrder>().DefaultDetailView.Layout).ToList());

    [Fact]
    public void ADerivedClass_GetsItsBaseColumns_WithItsOwnAdditions() =>
        Assert.Equal(["Number", "Customer", "OrderDate", "Customer.City", "Technician"],
            fixture.Class<ModelTestServiceOrder>().DefaultListView.Columns
                .Where(c => c.Index >= 0).OrderBy(c => c.Index).Select(c => c.PropertyName));
}
