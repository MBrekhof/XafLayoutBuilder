using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

public class KeywordHolder {
    public string @event { get; set; } = "";
    public string Normal { get; set; } = "";
}

public class CSharpLayoutPrinterTests {
    // Exactly the section 4 text, so builder -> spec -> print is a fixed point.
    const string Section4DetailCode = """
        LayoutBuilder<TestOrder>.Create()
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
                .TabFor(x => x.Lines, imageName: "BO_Order_Item")
                .TabFor(x => x.Attachments))
            .Hide(x => x.SyncToken)
            .Build()
        """;

    const string Section4ColumnsCode = """
        ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.Number, width: 90)
            .Column(x => x.Customer)
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
            .Hide(x => x.SyncToken)
            .Lookup(l => l
                .Column(x => x.Number)
                .Column(x => x.Customer))
            .Build()
        """;

    static string Norm(string s) => s.Replace("\r\n", "\n").Trim();

    [Fact]
    public void Section4Detail_RoundTripsToItsOwnSource() =>
        Assert.Equal(Norm(Section4DetailCode), Norm(CSharpLayoutPrinter.PrintDetail(LayoutBuilderTests.Section4Detail(), "TestOrder")));

    [Fact]
    public void Section4Columns_RoundTripsToItsOwnSource() =>
        Assert.Equal(Norm(Section4ColumnsCode), Norm(CSharpLayoutPrinter.PrintColumns(ListViewColumnsBuilderTests.Section4Columns(), "TestOrder")));

    [Fact]
    public void PrintClass_EmitsNamespace_BothMembers_AndNotesAsComments() {
        var code = Norm(CSharpLayoutPrinter.PrintClass("Sample.BusinessObjects", "TestOrder",
            LayoutBuilderTests.Section4Detail(), null, ["skipped: ActionContainer \"Save\""]));
        Assert.Contains("using XafLayoutBuilder.Core;", code);
        // Without the namespace the printed partial declares a different type and the member lambdas do not compile.
        Assert.Contains("namespace Sample.BusinessObjects;", code);
        Assert.Contains("// skipped: ActionContainer \"Save\"", code);
        Assert.Contains("public partial class TestOrder : ISupportViewLayoutCustomization {", code);
        Assert.Contains("    public static DetailLayoutSpec? BuildDetailViewLayout() =>\n        LayoutBuilder<TestOrder>.Create()", code);
        Assert.Contains("    public static ListColumnsSpec? BuildListViewColumns() =>\n        null;", code);
    }

    [Fact]
    public void PrintClass_WithoutNamespace_EmitsNoNamespaceLine() =>
        Assert.DoesNotContain("namespace", Norm(CSharpLayoutPrinter.PrintClass(null, "TestOrder", null, null)));

    [Fact]
    public void NonTabForTab_RootItem_GroupOptions_AndCaptionColumn_Print() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Item(x => x.Number)
            .Group("Outer", g => g.RelativeSize(40).Image("BO_Order")
                .Tabs("Inner", t => t.Tab("Custom", c => c.Caption("Custom \"tab\"").Item(x => x.Notes).Item(x => x.OrderDate))))
            .Build();
        var code = Norm(CSharpLayoutPrinter.PrintDetail(spec, "TestOrder"));
        Assert.Equal(Norm("""
            LayoutBuilder<TestOrder>.Create()
                .Item(x => x.Number)
                .Group("Outer", g => g
                    .RelativeSize(40)
                    .Image("BO_Order")
                    .Tabs("Inner", t => t
                        .Tab("Custom", g => g
                            .Caption("Custom \"tab\"")
                            .Item(x => x.Notes)
                            .Item(x => x.OrderDate))))
                .Build()
            """), code);
        var columns = ListViewColumnsBuilder<TestOrder>.Create().Column(x => x.Number, caption: "No.").Build();
        Assert.Contains(".Column(x => x.Number, caption: \"No.\")", CSharpLayoutPrinter.PrintColumns(columns, "TestOrder"));
    }

    [Fact]
    public void ControlCharactersInCaptions_AreEscaped() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("G", g => g.Caption("two\nlines\tand a \\ and \"quotes\"").Item(x => x.Number))
            .Build();
        var code = CSharpLayoutPrinter.PrintDetail(spec, "TestOrder");
        Assert.Contains("""".Caption("two\nlines\tand a \\ and \"quotes\"")"""", code);
        // The whole caption stays on its own line: a raw newline would break the string literal.
        var captionLine = code.Split('\n').First(l => l.Contains(".Caption(")).Trim();
        Assert.Equal(""".Caption("two\nlines\tand a \\ and \"quotes\"")""", captionLine);
    }

    [Fact]
    public void MemberNamedLikeAKeyword_GetsTheAtPrefix() {
        var spec = LayoutBuilder<KeywordHolder>.Create()
            .Group("G", g => g.Item(x => x.@event).Item(x => x.Normal))
            .Build();
        var code = CSharpLayoutPrinter.PrintDetail(spec, "KeywordHolder");
        Assert.Contains(".Item(x => x.@event)", code);
        Assert.Contains(".Item(x => x.Normal)", code);
    }

    // NEST-001: a column over a reference's member prints as the chained lambda, each segment escaped on its own.
    [Fact]
    public void NestedColumn_PrintsAChainedLambda() {
        var built = ListViewColumnsBuilder<TestOrder>.Create().Column(x => x.Customer!.Name).Hide(x => x.Customer!.City).Build();
        var code = CSharpLayoutPrinter.PrintColumns(built, "TestOrder");
        Assert.Contains(".Column(x => x.Customer.Name)", code);
        Assert.Contains(".Hide(x => x.Customer.City)", code);

        var raw = new ListColumnsSpec(typeof(TestOrder).FullName!, [new ColumnSpec("Customer.event")], []);
        Assert.Contains(".Column(x => x.Customer.@event)", CSharpLayoutPrinter.PrintColumns(raw, "TestOrder"));
    }

    // SORT-001: an explicit sort priority prints after the sort order, and only when set.
    [Fact]
    public void SortIndex_IsPrintedAfterTheSortOrder_OnlyWhenSet() {
        var spec = ListViewColumnsBuilder<TestOrder>.Create()
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending, sortIndex: 1)
            .Column(x => x.Customer, sort: ColumnSortOrder.Ascending, sortIndex: 0)
            .Build();
        var code = CSharpLayoutPrinter.PrintColumns(spec, "TestOrder");
        Assert.Contains(".Column(x => x.OrderDate, sort: ColumnSortOrder.Descending, sortIndex: 1)", code);
        Assert.Contains(".Column(x => x.Customer, sort: ColumnSortOrder.Ascending, sortIndex: 0)", code);
        Assert.DoesNotContain("sortIndex", CSharpLayoutPrinter.PrintColumns(ListViewColumnsBuilderTests.Section4Columns(), "TestOrder"));
    }

    // BAND-001: consecutive columns of one band print inside its .Band(...) call, with the caption only when it is set.
    [Fact]
    public void Bands_PrintAroundTheirColumns() {
        const string code = """
            ListViewColumnsBuilder<TestOrder>.Create()
                .Band("Identity", b => b
                    .Column(x => x.Number, width: 90)
                    .Column(x => x.Customer), caption: "Order")
                .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
                .Band("Remarks", b => b
                    .Column(x => x.Notes))
                .Build()
            """;
        var spec = ListViewColumnsBuilder<TestOrder>.Create()
            .Band("Identity", b => b.Column(x => x.Number, width: 90).Column(x => x.Customer), caption: "Order")
            .Column(x => x.OrderDate, sort: ColumnSortOrder.Descending)
            .Band("Remarks", b => b.Column(x => x.Notes))
            .Build();
        Assert.Equal(Norm(code), Norm(CSharpLayoutPrinter.PrintColumns(spec, "TestOrder")));
    }

    [Fact]
    public void EmptyGroupAndEmptyTabs_PrintABlockLambda() {
        // `g => g` is an expression, not a statement, so it does not convert to Action<GroupBuilder<T>>.
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("Empty", _ => { })
            .Tabs("NoTabs", _ => { })
            .Build();
        var code = CSharpLayoutPrinter.PrintDetail(spec, "TestOrder");
        Assert.Contains(".Group(\"Empty\", _ => { })", code);
        Assert.Contains(".Tabs(\"NoTabs\", _ => { })", code);
    }
}
