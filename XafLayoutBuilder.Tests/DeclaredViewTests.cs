using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Tests;

// VIEW-001: views declared in code (LayoutRegistry.AddDetailView / AddListView) are added to the generated layer, so XAF
// generates their items and columns and the updaters apply each view's own spec (ApplicationModelFixture).
[Collection(ApplicationModelCollection.Name)]
public class DeclaredViewTests(ApplicationModelFixture fixture) {
    [Fact]
    public void DeclaredDetailView_IsAddedForItsClass_WithItsOwnLayout() {
        var view = fixture.Model.Views[ModelTestDeclaredViews.DetailViewId] as IModelDetailView;
        Assert.NotNull(view);
        Assert.Equal(typeof(ModelTestOrder), view.ModelClass.TypeInfo.Type);
        Assert.Equal(["Main", "Compact", "Number", "OrderDate"], ApplicationModelFixture.LayoutIds(view.Layout).ToList());
        // The class's default DetailView keeps the class's own layout.
        Assert.Contains("Header", ApplicationModelFixture.LayoutIds(fixture.Class<ModelTestOrder>().DefaultDetailView.Layout));
    }

    [Fact]
    public void DeclaredListView_IsAddedForItsClass_WithItsOwnColumns() {
        var view = fixture.Model.Views[ModelTestDeclaredViews.ListViewId] as IModelListView;
        Assert.NotNull(view);
        Assert.Equal(typeof(ModelTestOrder), view.ModelClass.TypeInfo.Type);
        Assert.Equal(["OrderDate", "Number"], view.Columns.Where(c => c.Index >= 0).OrderBy(c => c.Index).Select(c => c.PropertyName));
    }

    // Codex review: a declaration rejected for a taken id must not hand its spec to the view that already has that id.
    [Fact]
    public void ARejectedDeclaration_DoesNotReplaceTheSpecOfTheViewThatHasItsId() {
        var modelClass = fixture.Class<ModelTestDegradedBroken>();
        LayoutRegistry.AddDetailView<ModelTestDegradedBroken>(modelClass.DefaultDetailView.Id, () =>
            LayoutBuilder<ModelTestDegradedBroken>.Create().Group("Declared", g => g.Item(x => x.Name)).Build());
        LayoutRegistry.AddListView<ModelTestDegradedBroken>(modelClass.DefaultListView.Id, () =>
            ListViewColumnsBuilder<ModelTestDegradedBroken>.Create().Column(x => x.Name).Build());

        var detail = LayoutSpecResolver.DetailForView(modelClass.DefaultDetailView, typeof(ModelTestDegradedBroken));
        Assert.Equal("Broken", Assert.IsType<LayoutGroupSpec>(Assert.Single(detail!.Nodes)).Id);
        Assert.Null(LayoutSpecResolver.DeclaredColumns(modelClass.DefaultListView, typeof(ModelTestDegradedBroken)));
    }

    // Codex re-review: a declaration rejected for a taken id stays a startup failure on every check, not only the first one,
    // which failed while XAF generated the views.
    [Fact]
    public void TheStartupCheck_RejectsADeclarationWhoseIdBelongsToAnotherView() {
        var listViewId = fixture.Class<ModelTestDegradedBroken>().DefaultListView.Id;
        var ex = Assert.Throws<LayoutSpecException>(() => LayoutStartupCheck.CheckDeclared(fixture.Model.Views, listViewId,
            new DeclaredView(typeof(ModelTestDegradedBroken), null, () => null)));
        Assert.StartsWith($"XLB005 {listViewId}", ex.Message);
    }

    // Codex review: two declarations of one id for different views are reported, not silently reduced to the last one.
    [Fact]
    public void TwoDeclarationsOfOneId_ForDifferentViews_AreXLB005() {
        const string id = "DeclaredViewTests_Conflict";
        LayoutRegistry.AddDetailView<ModelTestContact>(id, () => null);
        LayoutRegistry.AddListView<ModelTestCustomer>(id, () => null);
        var ex = Assert.Throws<LayoutSpecException>(() => DeclaredViewsUpdater.AddView(fixture.Model.Views, id, LayoutRegistry.Views[id]));
        Assert.StartsWith($"XLB005 {id}", ex.Message);
    }

    // Registering in a module constructor runs once per application instance; declaring the same view again is not a conflict.
    [Fact]
    public void DeclaringTheSameView_Again_ReplacesTheEarlierFactory() {
        const string id = "DeclaredViewTests_Again";
        Func<DetailLayoutSpec?> first = () => null;
        Func<DetailLayoutSpec?> second = () => null;
        LayoutRegistry.AddDetailView<ModelTestContact>(id, first);
        LayoutRegistry.AddDetailView<ModelTestContact>(id, second);
        Assert.Same(second, LayoutRegistry.Views[id].Detail);
    }

    [Fact]
    public void ADeclaredViewId_ThatAnotherViewAlreadyHas_IsXLB005() {
        var ex = Assert.Throws<LayoutSpecException>(() => DeclaredViewsUpdater.AddView(fixture.Model.Views, "ModelTestOrder_ListView",
            new DeclaredView(typeof(ModelTestOrder), null, () => null)));
        Assert.StartsWith("XLB005 ModelTestOrder_ListView", ex.Message);
    }
}
