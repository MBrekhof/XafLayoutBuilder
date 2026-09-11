using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

public class LayoutSpecJsonTests {
    [Fact]
    public void DetailLayout_RoundTrips_IncludingPolymorphicNodes() {
        var spec = LayoutBuilderTests.Section4Detail();
        var json = LayoutSpecJson.Serialize(spec);
        var back = LayoutSpecJson.Deserialize<DetailLayoutSpec>(json);

        Assert.Equal(json, LayoutSpecJson.Serialize(back));
        Assert.IsType<LayoutGroupSpec>(back.Nodes[0]);
        Assert.IsType<TabbedGroupSpec>(back.Nodes[2]);
        Assert.Equal(100, ((LayoutItemSpec)((LayoutGroupSpec)back.Nodes[1]).Children[0]).RelativeSize);
        Assert.Contains("\"direction\": \"Horizontal\"", json); // enums as strings, so BPG can hand-write it
        Assert.Contains("\"$type\": \"tabs\"", json);
    }

    [Fact]
    public void ListColumns_RoundTrips_IncludingLookup() {
        var spec = ListViewColumnsBuilderTests.Section4Columns();
        var json = LayoutSpecJson.Serialize(spec);
        var back = LayoutSpecJson.Deserialize<ListColumnsSpec>(json);

        Assert.Equal(json, LayoutSpecJson.Serialize(back));
        Assert.Equal(ColumnSortOrder.Descending, back.Columns[2].SortOrder);
        Assert.Equal(["Number", "Customer"], back.Lookup!.Columns.Select(c => c.Member));
    }
}
