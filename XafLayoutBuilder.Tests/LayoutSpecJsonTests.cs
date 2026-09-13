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
        Assert.Contains("\"direction\": \"Horizontal\"", json); // enums as strings, so a generator can hand-write it
        Assert.Contains("\"$type\": \"tabs\"", json);
    }

    // One document per type, the form the export writes and RegisterJson reads (JSON-001).
    [Fact]
    public void TypeDocument_RoundTrips_AndOmitsAMissingHalf() {
        var both = new LayoutSpecs(LayoutBuilderTests.Section4Detail(), ListViewColumnsBuilderTests.Section4Columns());
        var json = LayoutSpecJson.Serialize(both);

        Assert.Equal(json, LayoutSpecJson.Serialize(LayoutSpecJson.Deserialize<LayoutSpecs>(json)));
        Assert.Contains("\"detail\": {", json);
        Assert.Contains("\"columns\": {", json);

        var detailOnly = LayoutSpecJson.Serialize(both with { Columns = null });
        Assert.DoesNotContain("\"columns\"", detailOnly);
        Assert.Null(LayoutSpecJson.Deserialize<LayoutSpecs>(detailOnly).Columns);
    }

    [Fact]
    public void MalformedJson_ThrowsLayoutSpecException() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecJson.Deserialize<LayoutSpecs>("{ not json"));
        Assert.Contains("layout JSON", ex.Message);
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

    // BAND-001: bands are additive fields, left out of the JSON of a list that has none.
    [Fact]
    public void Bands_RoundTrip_AndAreLeftOutWhenThereAreNone() {
        var spec = ListViewColumnsBuilder<TestOrder>.Create()
            .Band("Identity", b => b.Column(x => x.Number), caption: "Order")
            .Column(x => x.OrderDate)
            .Build();
        var json = LayoutSpecJson.Serialize(spec);
        var back = LayoutSpecJson.Deserialize<ListColumnsSpec>(json);

        Assert.Equal(json, LayoutSpecJson.Serialize(back));
        Assert.Contains("\"band\": \"Identity\"", json);
        Assert.Equal([new BandSpec("Identity", "Order")], back.Bands!);
        Assert.DoesNotContain("band", LayoutSpecJson.Serialize(ListViewColumnsBuilderTests.Section4Columns()));
    }
}
