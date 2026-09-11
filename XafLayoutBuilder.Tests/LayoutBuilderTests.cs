using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

public class LayoutBuilderTests {
    // The exact example from XafLayoutBuilder-START.md section 4.
    internal static DetailLayoutSpec Section4Detail() =>
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
            .Build();

    [Fact]
    public void Section4Example_BuildsExpectedTree() {
        var spec = Section4Detail();

        Assert.Equal(typeof(TestOrder).FullName, spec.TypeName);
        Assert.Equal(["SyncToken"], spec.HiddenMembers);
        Assert.Equal(3, spec.Nodes.Count);

        var header = Assert.IsType<LayoutGroupSpec>(spec.Nodes[0]);
        Assert.Equal("Header", header.Id);
        Assert.Equal("Order", header.Caption);
        Assert.Equal(FlowDirection.Horizontal, header.Direction);
        Assert.False(header.Collapsible);
        Assert.Equal(["Number", "Customer", "OrderDate"], header.Children.Cast<LayoutItemSpec>().Select(i => i.Member));

        var details = Assert.IsType<LayoutGroupSpec>(spec.Nodes[1]);
        Assert.True(details.Collapsible);
        Assert.Equal(FlowDirection.Vertical, details.Direction);
        var notes = Assert.IsType<LayoutItemSpec>(Assert.Single(details.Children));
        Assert.Equal("Notes", notes.Member);
        Assert.Equal(100, notes.RelativeSize);

        var tabs = Assert.IsType<TabbedGroupSpec>(spec.Nodes[2]);
        Assert.Equal("Tabs", tabs.Id);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal("Lines", tabs.Tabs[0].Id);
        Assert.Equal("BO_Order_Item", tabs.Tabs[0].ImageName);
        Assert.Equal("Lines", Assert.IsType<LayoutItemSpec>(Assert.Single(tabs.Tabs[0].Children)).Member);
        Assert.Equal("Attachments", tabs.Tabs[1].Id);
        Assert.Null(tabs.Tabs[1].ImageName);
    }

    [Fact]
    public void ValueTypeMember_IsUnboxedToName() {
        var spec = LayoutBuilder<TestOrder>.Create().Group("G", g => g.Item(x => x.OrderDate)).Build();
        Assert.Equal("OrderDate", ((LayoutItemSpec)((LayoutGroupSpec)spec.Nodes[0]).Children[0]).Member);
    }

    [Fact]
    public void ItemPlacedTwice_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Group("A", g => g.Item(x => x.Number))
            .Group("B", g => g.Item(x => x.Number))
            .Build());
        Assert.Contains("'Number' is placed twice", ex.Message);
    }

    [Fact]
    public void ItemPlacedAndHidden_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Group("A", g => g.Item(x => x.SyncToken))
            .Hide(x => x.SyncToken)
            .Build());
        Assert.Contains("both placed and hidden", ex.Message);
    }

    [Fact]
    public void GroupIdUsedTwice_Throws_EvenAcrossNestingAndTabs() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Group("Main", g => g.Item(x => x.Number))
            .Tabs("T", t => t.Tab("Main", g => g.Item(x => x.Notes)))
            .Build());
        Assert.Contains("group id 'Main' is used twice", ex.Message);
    }

    [Fact]
    public void NestedMemberPath_IsRejectedAtCallTime() {
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutBuilder<TestOrder>.Create().Group("G", g => g.Item(x => x.Customer!.Name)));
        Assert.Contains("not a simple member access", ex.Message);
    }

    [Fact]
    public void MethodCall_IsRejected() {
        Assert.Throws<LayoutSpecException>(() =>
            LayoutBuilder<TestOrder>.Create().Hide(x => x.Number.ToString()));
    }

    [Fact]
    public void NestedGroupsInsideGroup_AreKept() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("Outer", g => g
                .Group("Inner", i => i.Item(x => x.Number))
                .Item(x => x.Notes))
            .Build();
        var outer = (LayoutGroupSpec)spec.Nodes[0];
        Assert.Equal("Inner", ((LayoutGroupSpec)outer.Children[0]).Id);
        Assert.Equal("Notes", ((LayoutItemSpec)outer.Children[1]).Member);
    }
}
