using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

// Codex review of sessions 2-3: id collisions, immutability, untested surface, registry guard.
public class ReviewFollowUpTests {
    [Fact]
    public void GroupAndItemWithSameId_AsSiblings_Throws() {
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Group("Details", g => g
                .Group("Notes", i => i.Item(x => x.Number))
                .Item(x => x.Notes))
            .Build());
        Assert.Contains("'Notes' names both a group and an item under the same parent", ex.Message);
    }

    [Fact]
    public void GroupAndItemWithSameId_AsParentAndChild_IsAllowed() {
        // What TabFor produces: group "Lines" holding item "Lines". XAF only requires sibling uniqueness.
        var spec = LayoutBuilder<TestOrder>.Create().Group("Notes", g => g.Item(x => x.Notes)).Build();
        Assert.Equal("Notes", ((LayoutGroupSpec)spec.Nodes[0]).Id);
    }

    [Fact]
    public void TabbedGroupAndGroupWithSameId_Throws() {
        Assert.Throws<LayoutSpecException>(() => LayoutBuilder<TestOrder>.Create()
            .Group("Tabs", g => g.Item(x => x.Number))
            .Tabs("Tabs", t => t.TabFor(x => x.Lines))
            .Build());
    }

    [Fact]
    public void BuiltSpec_CannotBeMutatedThroughACast() {
        var spec = LayoutBuilderTests.Section4Detail();
        Assert.Throws<NotSupportedException>(() => ((IList<LayoutNodeSpec>)spec.Nodes).Add(new LayoutItemSpec("X")));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)spec.HiddenMembers).Clear());
        var header = (LayoutGroupSpec)spec.Nodes[0];
        Assert.Throws<NotSupportedException>(() => ((IList<LayoutNodeSpec>)header.Children).RemoveAt(0));
        var columns = ListViewColumnsBuilderTests.Section4Columns();
        Assert.Throws<NotSupportedException>(() => ((IList<ColumnSpec>)columns.Columns).Clear());
    }

    [Fact]
    public void RecordConstructedFromCallerList_DoesNotAliasIt() {
        var list = new List<LayoutNodeSpec> { new LayoutItemSpec("Number") };
        var g = new LayoutGroupSpec("G", list);
        list.Add(new LayoutItemSpec("Notes"));
        Assert.Single(g.Children);
    }

    [Fact]
    public void DeserialisedSpec_IsFrozenToo() {
        var back = LayoutSpecJson.Deserialize<DetailLayoutSpec>(LayoutSpecJson.Serialize(LayoutBuilderTests.Section4Detail()));
        Assert.Throws<NotSupportedException>(() => ((IList<LayoutNodeSpec>)back.Nodes).Clear());
    }

    [Fact]
    public void GroupOptions_RelativeSizeImage_AndNestedTabs_AreKept() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Group("Outer", g => g
                .RelativeSize(40)
                .Image("BO_Order")
                .Tabs("Inner", t => t
                    .Tab("Custom", c => c.Caption("Custom tab").Item(x => x.Number).Item(x => x.Notes))
                    .TabFor(x => x.Lines, caption: "Order lines")))
            .Build();
        var outer = (LayoutGroupSpec)spec.Nodes[0];
        Assert.Equal(40, outer.RelativeSize);
        Assert.Equal("BO_Order", outer.ImageName);
        var tabs = Assert.IsType<TabbedGroupSpec>(Assert.Single(outer.Children));
        Assert.Equal("Custom", tabs.Tabs[0].Id);
        Assert.Equal("Custom tab", tabs.Tabs[0].Caption);
        Assert.Equal(["Number", "Notes"], tabs.Tabs[0].Children.Cast<LayoutItemSpec>().Select(i => i.Member));
        Assert.Equal("Order lines", tabs.Tabs[1].Caption);
    }

    [Fact]
    public void ColumnCaption_IsKept() {
        var spec = ListViewColumnsBuilder<TestOrder>.Create().Column(x => x.Number, caption: "No.").Build();
        Assert.Equal("No.", spec.Columns[0].Caption);
    }

    [Fact]
    public void Members_ListsPlacedAndHiddenMembers_DepthFirst() {
        Assert.Equal(["Number", "Customer", "OrderDate", "Notes", "Lines", "Attachments", "SyncToken"],
            LayoutBuilderTests.Section4Detail().Members());
        Assert.Equal(["Number", "Customer", "OrderDate", "SyncToken", "Number", "Customer"],
            ListViewColumnsBuilderTests.Section4Columns().Members());
    }

    [Fact]
    public void EnsureMembersExist_NamesTheMissingOnes() {
        LayoutSpecChecks.EnsureMembersExist(typeof(TestOrder), LayoutBuilderTests.Section4Detail().Members());
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.EnsureMembersExist(typeof(TestCustomer), LayoutBuilderTests.Section4Detail().Members()));
        Assert.Contains("TestCustomer has no member(s) Number, Customer", ex.Message);
    }
}
