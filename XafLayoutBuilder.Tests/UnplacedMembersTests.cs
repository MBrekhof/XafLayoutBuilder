using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

public class UnplacedMembersTests {
    [Fact]
    public void TheDefaultIsToFail() {
        Assert.Null(LayoutBuilderTests.Section4Detail().UnplacedGroupId);
        Assert.Null(LayoutBuilder<TestOrder>.Create().Unplaced(UnplacedMembers.Fail).Build().UnplacedGroupId);
    }

    [Fact]
    public void AppendToGroup_IsCarriedOnTheSpec() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("Header", g => g.Item(x => x.Number))
            .Unplaced(UnplacedMembers.AppendToGroup("Other"))
            .Build();
        Assert.Equal("Other", spec.UnplacedGroupId);
        // The catch-all does not count as a placed member: only what the layout mentions is in Members().
        Assert.Equal(["Number"], spec.Members());
    }

    [Fact]
    public void AppendToGroup_ReusingAGroupId_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Group("Other", g => g.Item(x => x.Number))
            .Unplaced(UnplacedMembers.AppendToGroup("Other"))
            .Build());
        Assert.Contains("'Other' is already used in this layout", ex.Message);
    }

    [Fact]
    public void AppendToGroup_ReusingARootItemName_Throws() {
        // The catch-all becomes a sibling of the root items, and XAF requires unique ids among siblings.
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Item(x => x.Number)
            .Unplaced(UnplacedMembers.AppendToGroup("Number"))
            .Build());
        Assert.Contains("'Number' is already used in this layout", ex.Message);
    }

    [Fact]
    public void AppendToGroup_ReusingAMemberNameInsideAGroup_IsFine() {
        // Only the root level shares a parent with the catch-all group.
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("Header", g => g.Item(x => x.Number))
            .Unplaced(UnplacedMembers.AppendToGroup("Number"))
            .Build();
        Assert.Equal("Number", spec.UnplacedGroupId);
    }

    [Fact]
    public void AppendToGroup_WithoutAnId_Throws() =>
        Assert.Throws<LayoutSpecException>(() => UnplacedMembers.AppendToGroup("  "));

    [Fact]
    public void ThePolicyIsPrintedAndRoundTripsThroughJson() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("Header", g => g.Item(x => x.Number))
            .Unplaced(UnplacedMembers.AppendToGroup("Other"))
            .Build();
        var code = CSharpLayoutPrinter.PrintDetail(spec, "TestOrder");
        Assert.Contains(".Unplaced(UnplacedMembers.AppendToGroup(\"Other\"))", code);
        Assert.True(code.IndexOf(".Unplaced(", StringComparison.Ordinal) < code.IndexOf(".Build()", StringComparison.Ordinal));
        Assert.Equal("Other", LayoutSpecJson.Deserialize<DetailLayoutSpec>(LayoutSpecJson.Serialize(spec)).UnplacedGroupId);
    }
}
