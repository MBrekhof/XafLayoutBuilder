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
        Assert.Contains("'Other' is used twice (once by Unplaced)", ex.Message);
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
