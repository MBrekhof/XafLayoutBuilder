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

    // NEST-001: a column may follow a reference, named the way XAF's own generator names such a column ("Customer.City").
    [Fact]
    public void NestedColumnHideAndLookup_BuildDottedPaths() {
        var spec = ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.Customer!.Name, caption: "Customer name")
            .Hide(x => x.Customer!.City)
            .Lookup(l => l.Column(x => x.Customer!.Name))
            .Build();

        Assert.Equal("Customer.Name", Assert.Single(spec.Columns).Member);
        Assert.Equal(["Customer.City"], spec.HiddenMembers);
        Assert.Equal("Customer.Name", Assert.Single(spec.Lookup!.Columns).Member);
    }

    // SORT-001: sort priority can differ from column order.
    [Fact]
    public void SortIndex_SetsSortPriority_IndependentlyOfColumnOrder() {
        var spec = ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending, sortIndex: 1)
            .Column(x => x.Customer, sort: ColumnSortOrder.Ascending, sortIndex: 0)
            .Column(x => x.Number)
            .Build();
        Assert.Equal(new int?[] { 1, 0, null }, spec.Columns.Select(c => c.SortIndex));
    }

    // BAND-001: a band groups the columns declared inside it; they keep their place in the column order.
    [Fact]
    public void Band_SetsTheBandOnItsColumns_AndDeclaresTheBand() {
        var spec = ListViewColumnsBuilder<TestOrder>.Create()
            .Band("Identity", b => b.Column(x => x.Number).Column(x => x.Customer), caption: "Order")
            .Column(x => x.OrderDate)
            .Build();
        Assert.Equal(["Number", "Customer", "OrderDate"], spec.Columns.Select(c => c.Member));
        Assert.Equal(new[] { "Identity", "Identity", null }, spec.Columns.Select(c => c.Band));
        Assert.Equal([new BandSpec("Identity", "Order")], spec.Bands!);
        Assert.Null(Section4Columns().Bands);
    }

    [Fact]
    public void ABandInsideABand_OrALookupInsideABand_Throws() {
        Assert.Contains("cannot be nested", Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create()
            .Band("A", b => b.Band("B", c => c.Column(x => x.Number)))).Message);
        Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create()
            .Band("A", b => b.Lookup(l => l.Column(x => x.Number))));
    }

    [Fact]
    public void NestedPathThroughAMethodCall_IsRejected() {
        var ex = Assert.Throws<LayoutSpecException>(() => ListViewColumnsBuilder<TestOrder>.Create().Column(x => x.Customer!.Name.Trim()));
        Assert.Contains("not a member access", ex.Message);
    }
}
