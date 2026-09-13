using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;
using XafFlow = DevExpress.ExpressApp.Layout.FlowDirection;

namespace XafLayoutBuilder.Tests;

// MODEL-001: DetailViewLayoutUpdater against a real Application Model (ApplicationModelFixture).
[Collection(ApplicationModelCollection.Name)]
public class DetailViewLayoutUpdaterTests(ApplicationModelFixture fixture) {
    static IModelLayoutGroup Main(IModelDetailView view) => Assert.IsAssignableFrom<IModelLayoutGroup>(Assert.Single(view.Layout));

    [Fact]
    public void BuilderNodes_ReplaceTheGeneratedLayout_InSpecOrder() {
        var main = Main(fixture.Class<ModelTestOrder>().DefaultDetailView);
        Assert.Equal("Main", main.Id);
        Assert.True(main.ShowCaption != true);
        Assert.Equal(["Header", "Details", "Tabs"], main.OrderBy(e => e.Index).Select(e => e.Id));
    }

    [Fact]
    public void GroupSettings_CaptionFlowCollapsibleAndRelativeSize_AreApplied() {
        var main = Main(fixture.Class<ModelTestOrder>().DefaultDetailView);

        var header = Assert.IsAssignableFrom<IModelLayoutGroup>(main["Header"]);
        Assert.Equal("Order", header.Caption);
        Assert.True(header.ShowCaption == true);
        Assert.Equal(XafFlow.Horizontal, header.Direction);
        Assert.Equal(["Number", "Customer", "OrderDate"], header.OrderBy(e => e.Index).Select(e => e.Id));
        Assert.Equal(["Number", "Customer", "OrderDate"], header.OrderBy(e => e.Index).Cast<IModelLayoutViewItem>().Select(i => i.ViewItem?.Id));

        var details = Assert.IsAssignableFrom<IModelLayoutGroup>(main["Details"]);
        Assert.True(details.IsCollapsibleGroup);
        Assert.True(details.ShowCaption == true);
        Assert.Equal(100d, Assert.IsAssignableFrom<IModelLayoutViewItem>(Assert.Single(details)).RelativeSize);
    }

    [Fact]
    public void TabFor_BuildsATabHoldingTheCollection_WithoutTheItemCaption() {
        var tabs = Assert.IsAssignableFrom<IModelTabbedGroup>(Main(fixture.Class<ModelTestOrder>().DefaultDetailView)["Tabs"]);
        var tab = Assert.Single(tabs);
        Assert.Equal("Lines", tab.Id);
        var item = Assert.IsAssignableFrom<IModelLayoutViewItem>(Assert.Single(tab));
        Assert.Equal("Lines", item.Id);
        Assert.True(item.ShowCaption == false);
    }

    [Fact]
    public void HiddenMember_IsNotPlacedAnywhere() =>
        Assert.DoesNotContain("SyncToken", ApplicationModelFixture.LayoutIds(fixture.Class<ModelTestOrder>().DefaultDetailView.Layout));

    [Fact]
    public void Unplaced_AppendsTheLeftoverMembersToAMarkedCatchAllGroup() {
        var main = Main(fixture.Class<ModelTestContact>().DefaultDetailView);
        Assert.Equal(["Identification", "Other"], main.OrderBy(e => e.Index).Select(e => e.Id));
        var other = Assert.IsAssignableFrom<IModelLayoutGroup>(main["Other"]);
        Assert.Equal("Other", other.Caption);
        Assert.True(((ModelNode)other).GetValue<bool>(DetailViewLayoutUpdater.CatchAllMarker));
        Assert.Equal(["Email", "Phone"], other.Select(e => e.Id).Order());
    }

    // Both broken views are read by the fixture before any test runs (see ApplicationModelFixture).
    [Fact]
    public void RejectedSpec_WithFailFastOn_ThrowsWhereTheLayoutIsRead() {
        var ex = Assert.IsType<LayoutSpecException>(fixture.StrictBrokenLayoutError);
        Assert.Contains("XLB001", ex.Message);
    }

    // Check before mutate: a spec the updater rejects leaves XAF's own generated layout as it was.
    [Fact]
    public void RejectedSpec_WithFailFastOff_LeavesTheGeneratedLayoutIntact() {
        Assert.Contains("SimpleEditors", fixture.DegradedBrokenLayoutIds);
        Assert.DoesNotContain("Broken", fixture.DegradedBrokenLayoutIds);
    }
}
