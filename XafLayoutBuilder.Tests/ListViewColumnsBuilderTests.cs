using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

public class ListViewColumnsBuilderTests {
    // The exact example from XafLayoutBuilder-START.md section 4.
    internal static ListColumnsSpec Section4Columns() =>
        ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.Number, width: 90)
            .Column(x => x.Customer)
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
            .Hide(x => x.SyncToken)
            .Lookup(l => l
                .Column(x => x.Number)
                .Column(x => x.Customer))
            .Build();

    [Fact]
    public void Section4Example_BuildsExpectedColumns() {
        var spec = Section4Columns();

        Assert.Equal(typeof(TestOrder).FullName, spec.TypeName);
        Assert.Equal(["Number", "Customer", "OrderDate"], spec.Columns.Select(c => c.Member));
        Assert.Equal(90, spec.Columns[0].Width);
        Assert.Null(spec.Columns[1].Width);
        Assert.Equal(ColumnSortOrder.None, spec.Columns[1].SortOrder);
        Assert.Equal(ColumnSortOrder.Descending, spec.Columns[2].SortOrder);
        Assert.Equal(["SyncToken"], spec.HiddenMembers);

        Assert.NotNull(spec.Lookup);
        Assert.Equal(["Number", "Customer"], spec.Lookup.Columns.Select(c => c.Member));
        Assert.Empty(spec.Lookup.HiddenMembers);
        Assert.Null(spec.Lookup.Lookup);
    }

    [Fact]
    public void NoLookup_LeavesLookupNull() {
        Assert.Null(ListViewColumnsBuilder<TestOrder>.Create().Column(x => x.Number).Build().Lookup);
    }

    [Fact]
    public void ColumnListedTwice_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.Number).Column(x => x.Number).Build());
        Assert.Contains("'Number' is listed twice", ex.Message);
    }

    [Fact]
    public void ColumnListedAndHidden_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.Number).Hide(x => x.Number).Build());
        Assert.Contains("both listed and hidden", ex.Message);
    }

    [Fact]
    public void NestedLookup_Throws() {
        Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create()
            .Lookup(l => l.Lookup(_ => { })).Build());
    }

    [Fact]
    public void LookupValidation_RunsOnLookupColumnsToo() {
        Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create()
            .Lookup(l => l.Column(x => x.Number).Column(x => x.Number)).Build());
    }
}
