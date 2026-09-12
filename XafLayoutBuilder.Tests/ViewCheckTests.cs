using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Tests;

// The detail updater checks a spec against the view's items before it touches the model, so a rejected spec leaves
// XAF's generated layout intact. XAF marks generation done even when an updater throws (ModelNode.cs 2219-2229), so
// a check that ran halfway through the rebuild would leave a half-applied layout behind for good.
public class ViewCheckTests {
    const string View = "TestOrder_DetailView";
    static readonly string[] AllItems = ["Number", "Customer", "OrderDate", "Notes", "SyncToken", "Lines", "Attachments"];

    [Fact]
    public void Section4Layout_AgainstItsOwnItems_LeavesNothingUnplaced() =>
        Assert.Empty(LayoutSpecChecks.CheckAgainstView(LayoutBuilderTests.Section4Detail(), View, AllItems, AllItems));

    [Fact]
    public void PlacedMemberWithoutAViewItem_IsXlb001() {
        var items = AllItems.Except(["Notes"]).ToArray();
        var ex = Assert.Throws<LayoutSpecException>(() =>
            LayoutSpecChecks.CheckAgainstView(LayoutBuilderTests.Section4Detail(), View, items, items));
        Assert.StartsWith("XLB001 TestOrder_DetailView: member 'Notes'", ex.Message);
    }

    [Fact]
    public void VisibleEditorNeitherPlacedNorHidden_IsXlb002_NamingThemInItemOrder() {
        var spec = LayoutBuilder<TestOrder>.Create().Group("G", g => g.Item(x => x.Number)).Hide(x => x.SyncToken).Build();
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutSpecChecks.CheckAgainstView(spec, View, AllItems, AllItems));
        Assert.StartsWith("XLB002 TestOrder_DetailView: members not placed and not hidden: Customer, OrderDate, Notes, Lines, Attachments.", ex.Message);
        Assert.Contains("to TestOrder's layout", ex.Message);
    }

    [Fact]
    public void InvisibleEditor_NeedsNoPlacing_ButCanStillBePlaced() {
        var numberOnly = LayoutBuilder<TestOrder>.Create().Group("G", g => g.Item(x => x.Number)).Build();
        Assert.Empty(LayoutSpecChecks.CheckAgainstView(numberOnly, View, AllItems, visibleEditors: ["Number"]));
        var syncTokenOnly = LayoutBuilder<TestOrder>.Create().Group("G", g => g.Item(x => x.SyncToken)).Build();
        Assert.Empty(LayoutSpecChecks.CheckAgainstView(syncTokenOnly, View, AllItems, visibleEditors: []));
    }

    [Fact]
    public void WithACatchAll_TheLeftoversComeBackInItemOrder() {
        var spec = LayoutBuilder<TestOrder>.Create()
            .Tabs("T", t => t.TabFor(x => x.Lines))
            .Hide(x => x.SyncToken)
            .Unplaced(UnplacedMembers.AppendToGroup("Other"))
            .Build();
        Assert.Equal(["Number", "Customer", "OrderDate", "Notes", "Attachments"],
            LayoutSpecChecks.CheckAgainstView(spec, View, AllItems, AllItems));
    }

    [Fact]
    public void PlacedMembers_WalksTheTree_AndLeavesHiddenOut() =>
        Assert.Equal(["Number", "Customer", "OrderDate", "Notes", "Lines", "Attachments"],
            LayoutBuilderTests.Section4Detail().PlacedMembers());
}
