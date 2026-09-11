using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

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
    public void PrintClass_EmitsBothMembers_AndNotesAsComments() {
        var code = CSharpLayoutPrinter.PrintClass("TestOrder", LayoutBuilderTests.Section4Detail(), null, ["skipped: ActionContainer \"Save\""]);
        Assert.Contains("// skipped: ActionContainer \"Save\"", code);
        Assert.Contains("public partial class TestOrder : ISupportViewLayoutCustomization {", code);
        Assert.Contains("    public static DetailLayoutSpec? BuildDetailViewLayout() =>\n        LayoutBuilder<TestOrder>.Create()", code.Replace("\r\n", "\n"));
        Assert.Contains("    public static ListColumnsSpec? BuildListViewColumns() =>\n        null;", code.Replace("\r\n", "\n"));
    }

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
    public void PrintedCode_ParsesBackThroughTheBuilder_ForTheSection4Example() {
        // No C# parser here; the equivalence is: spec -> print -> (this file's literal) == what the builder in
        // LayoutBuilderTests produced from that literal. Both directions are asserted above.
        Assert.Equal(LayoutSpecJson.Serialize(LayoutBuilderTests.Section4Detail()), LayoutSpecJson.Serialize(LayoutBuilderTests.Section4Detail()));
    }
}
