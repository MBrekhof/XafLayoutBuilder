using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Tests;

// RECHECK-001: XAF marks a node generated even when its updater threw, so a startup check that only touches the view
// passes the second time. The check repeats the checks that need the view instead. ModelTestStrictBroken's layout was
// generated, and failed with XLB001, in ApplicationModelFixture's constructor, which makes every run here a repeat.
[Collection(ApplicationModelCollection.Name)]
public class StartupCheckTests(ApplicationModelFixture fixture) {
    [Fact]
    public void ARepeatedCheck_StillReportsABrokenLayout() {
        for (var run = 1; run <= 2; run++) {
            var ex = Assert.Throws<LayoutSpecException>(() => LayoutStartupCheck.Check(fixture.Model));
            Assert.Contains("XLB001 ModelTestStrictBroken_DetailView", ex.Message);
        }
    }

    // Codex review: a layout the updater applied is not checked again against the merged model, where a module or
    // administrator difference may since have removed an editor the spec places. That is a customisation, not a builder error.
    [Fact]
    public void AnAppliedLayout_IsNotBlamedForALaterLayersChange() {
        var view = fixture.Class<ModelTestLaterRemoval>().DefaultDetailView;
        _ = view.Layout.Count;
        view.Items[nameof(ModelTestLaterRemoval.Notes)]!.Remove();

        var ex = Assert.Throws<LayoutSpecException>(() => LayoutStartupCheck.Check(fixture.Model)); // StrictBroken still fails
        Assert.DoesNotContain("ModelTestLaterRemoval_DetailView", ex.Message);
    }
}
