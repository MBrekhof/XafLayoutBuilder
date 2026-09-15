using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Utils;
using XafLayoutBuilder.Module;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-002: XAF Blazor 26.1 warms the application up by default and collapses each application's model, which then
// answers GetValue from a value cache (docs/api-notes.md, "Runtime Model Editor"). ApplicationModelFixture's model is not
// warmed up, so these tests build their own the way the running application does. The switches are process-wide, hence
// this collection (its tests never run in parallel) and the try/finally, as DevExpress's ModelApplicationTestHelper does
// (Model/ModelApplicationTestHelper.cs 153-164).
[Collection(ApplicationModelCollection.Name)]
public class WarmedUpModelTests(ApplicationModelFixture fixture) {
    [Fact]
    public void TheModel_IsCollapsedLikeXafBlazors() =>
        WithWarmedUpModels(build => Assert.True(build(ModelStoreBase.Empty).IsCollapsed));

    // What the editor has to live with (DXSUPPORT-001): ClearValue leaves the cached value, and no public call refreshes it.
    [Fact]
    public void ClearValue_LeavesTheCachedValue_UntilTheModelIsBuiltAgain() => WithWarmedUpModels(build => {
        var view = ContactListView(build(ModelStoreBase.Empty));
        ModelEditing.SetText(view, "Caption", "Saved earlier");

        ModelEditing.Reset(view, "Caption");
        Assert.False(((ModelNode)view).IsValueModified("Caption"));
        Assert.Equal("Saved earlier", view.Caption);
    });

    // Hence Save reloads the page: the new circuit builds the model again from the saved differences, where a reset is right.
    [Fact]
    public void AModelBuiltFromTheSavedDifferences_ShowsASavedReset() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var original = ContactListView(model).Caption;
        ModelEditing.SetText(ContactListView(model), "Caption", "Saved earlier");
        var saved = build(new StringModelStore(model.LastLayer.Xml));
        Assert.Equal("Saved earlier", ContactListView(saved).Caption);

        ModelEditing.Reset(ContactListView(saved), "Caption");
        Assert.Equal(original, ContactListView(build(new StringModelStore(saved.LastLayer.Xml))).Caption);
    });

    // DXSUPPORT-001 item 7: the database store skips an aspect whose differences are gone and keeps its old row, so the editor
    // finds those aspects and blanks their stored rows after it saves (StoredAspectCleanup).
    [Fact]
    // The default aspect keeps the node structure an edit created, so the aspect that empties is a culture's: in the gate the
    // en-US row, which held only the localized caption.
    public void EmptyAspects_NameTheAspectsWhoseDifferencesAreGone() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect("de");
        model.SetCurrentAspect("de");
        var view = ContactListView(model);
        ModelEditing.SetText(view, "Caption", "Kontakte");
        Assert.DoesNotContain("de", ModelEditing.EmptyAspects(model.LastLayer));

        ModelEditing.Reset(view, "Caption");
        Assert.Contains("de", ModelEditing.EmptyAspects(model.LastLayer));
        Assert.DoesNotContain("", ModelEditing.EmptyAspects(model.LastLayer));
    });

    // Codex re-review: the circuit's model can be stale (another tab saved a localized caption since), so the cleanup blanks
    // only the aspects this save emptied, never every aspect that happens to be empty in this circuit's model.
    [Fact]
    public void AspectsEmptiedBy_AChangeThatEmptiesNothing_IsEmpty() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect("de");
        model.SetCurrentAspect("de");
        Assert.Contains("de", ModelEditing.EmptyAspects(model.LastLayer));

        Assert.Empty(ModelEditing.AspectsEmptiedBy(model.LastLayer, () => { }));
    });

    [Fact]
    public void AspectsEmptiedBy_AResetOfTheLastValueOfAnAspect_NamesThatAspect() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect("de");
        model.SetCurrentAspect("de");
        var view = ContactListView(model);
        ModelEditing.SetText(view, "Caption", "Kontakte");

        Assert.Equal(["de"], ModelEditing.AspectsEmptiedBy(model.LastLayer, () => ModelEditing.Reset(view, "Caption")));
    });

    // Codex review 3: a save or cleanup that throws must not lose the aspects the applied edits emptied; a retried Save has
    // no pending edits left, so only the session can still say which stored rows to blank. They stay until Saved().
    [Fact]
    public void Session_KeepsTheAspectsItsEditsEmptied_UntilSaved() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect("de");
        model.SetCurrentAspect("de");
        var view = ContactListView(model);
        ModelEditing.SetText(view, "Caption", "Kontakte");
        var session = new ModelEditSession();
        session.Reset(view, "Caption");

        session.Apply(model.LastLayer);
        Assert.Equal(["de"], session.EmptiedAspects);
        session.Apply(model.LastLayer); // a retry after the save threw: nothing pending, the aspect is already empty
        Assert.Equal(["de"], session.EmptiedAspects);

        session.Saved();
        Assert.Empty(session.EmptiedAspects);
    });

    // Codex review 4: after a failed save the user can fill the emptied aspect again before retrying; the retried save must
    // not then blank the stored row that now holds the replacement.
    [Fact]
    public void Session_ForgetsAnEmptiedAspect_ThatALaterEditFillsAgain() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect("de");
        model.SetCurrentAspect("de");
        var view = ContactListView(model);
        ModelEditing.SetText(view, "Caption", "Kontakte");
        var session = new ModelEditSession();
        session.Reset(view, "Caption");
        session.Apply(model.LastLayer);
        Assert.Equal(["de"], session.EmptiedAspects);

        session.SetText(view, "Caption", "Ansprechpartner"); // the save threw; a replacement before the retry
        session.Apply(model.LastLayer);
        Assert.Empty(session.EmptiedAspects);
    });

    // MODELEDITOR-004: a node added in the editor is removed again when the popup closes without Save. In the warmed-up
    // model the removal has to show in the children list (ClearValue's cache trap must not repeat for nodes).
    [Fact]
    public void ANodeAddedAndRemoved_IsGoneFromTheChildren() => WithWarmedUpModels(build => {
        var columns = ContactListView(build(ModelStoreBase.Empty)).Columns;
        var added = ModelEditing.AddChild(columns, typeof(IModelColumn), "Transient");
        Assert.Contains(ModelEditing.Children(columns), n => ModelEditing.Id(n) == "Transient");

        added.Remove();
        Assert.DoesNotContain(ModelEditing.Children(columns), n => ModelEditing.Id(n) == "Transient");
    });

    // Codex review: a required value reset on an added node keeps returning its cached value, so the session counts the reset
    // itself and Save refuses the node.
    [Fact]
    public void Session_Apply_RefusesAnAddedNodeWhoseRequiredValueWasReset() => WithWarmedUpModels(build => {
        var columns = ContactListView(build(ModelStoreBase.Empty)).Columns;
        var session = new ModelEditSession();
        var column = session.AddChild(columns, typeof(IModelColumn), "ResetOnAdded");
        session.SetText(column, "PropertyName", nameof(ModelTestContact.Name));
        session.Reset(column, "PropertyName");

        var ex = Assert.Throws<InvalidOperationException>(() => session.Apply());
        Assert.Contains("PropertyName", ex.Message);
    });

    // MODELEDITOR-007 review: a node saved in an earlier session is not in this session's added collection. Clearing its
    // required reference must still refuse Save: otherwise the DetailView loses ClassName and disappears on reload.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_Apply_RefusesARequiredReferenceClearOnASavedCustomView(bool emptyChoice) => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var create = new ModelEditSession();
        var view = create.AddChild(((IModelApplication)model).Views, typeof(IModelDetailView), "SavedCustomView");
        var modelClass = ModelEditing.Path(ContactListView(model).ModelClass);
        create.SetText(view, "ModelClass", modelClass);
        create.Apply(model.LastLayer);
        var loaded = build(new StringModelStore(model.LastLayer.Xml));
        var savedView = ((IModelApplication)loaded).Views["SavedCustomView"]!;
        var before = loaded.LastLayer.Xml;
        var edit = new ModelEditSession();

        if (emptyChoice) edit.SetText(savedView, "ModelClass", "");
        else edit.Reset(savedView, "ModelClass");

        var error = Assert.Throws<InvalidOperationException>(() => edit.Apply(loaded.LastLayer));
        Assert.Contains("Views/SavedCustomView", error.Message);
        Assert.Contains("ModelClass", error.Message);
        Assert.Contains("ModelClass", edit.MissingRequired(savedView));
        Assert.Equal(before, loaded.LastLayer.Xml);
        Assert.True(edit.TryGetPending(savedView, "ModelClass", out _, out _));

        edit.SetText(savedView, "ModelClass", modelClass);
        Assert.DoesNotContain("ModelClass", edit.MissingRequired(savedView));
        edit.Apply(loaded.LastLayer);
        var reloaded = build(new StringModelStore(loaded.LastLayer.Xml));
        var restored = Assert.IsAssignableFrom<IModelDetailView>(((IModelApplication)reloaded).Views["SavedCustomView"]);
        Assert.Equal(modelClass, ModelEditing.Path(restored.ModelClass));
    });

    [Fact]
    public void Session_Apply_RefusesARequiredStringResetOnASavedCustomColumn() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var create = new ModelEditSession();
        var column = create.AddChild(ContactListView(model).Columns, typeof(IModelColumn), "SavedCustomColumn");
        create.SetText(column, "PropertyName", nameof(ModelTestContact.Name));
        create.Apply(model.LastLayer);
        var loaded = build(new StringModelStore(model.LastLayer.Xml));
        var savedColumn = ContactListView(loaded).Columns["SavedCustomColumn"]!;
        var before = loaded.LastLayer.Xml;
        var edit = new ModelEditSession();

        edit.Reset(savedColumn, "PropertyName");

        var error = Assert.Throws<InvalidOperationException>(() => edit.Apply(loaded.LastLayer));
        Assert.Contains("PropertyName", error.Message);
        Assert.Contains("PropertyName", edit.MissingRequired(savedColumn));
        Assert.Equal(before, loaded.LastLayer.Xml);
    });

    [Fact]
    public void Session_Apply_RefusesARequiredResetBelowASavedCustomView() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var create = new ModelEditSession();
        create.Clone(ContactListView(model).ModelClass.DefaultDetailView, "SavedParentView");
        create.Apply(model.LastLayer);
        var loaded = build(new StringModelStore(model.LastLayer.Xml));
        var savedView = (IModelDetailView)((IModelApplication)loaded).Views["SavedParentView"]!;
        var item = Assert.IsAssignableFrom<IModelMemberViewItem>(savedView.Items[nameof(ModelTestContact.Name)]);
        var before = loaded.LastLayer.Xml;
        var edit = new ModelEditSession();

        edit.Reset(item, "PropertyName");

        var error = Assert.Throws<InvalidOperationException>(() => edit.Apply(loaded.LastLayer));
        Assert.Contains("PropertyName", error.Message);
        Assert.Equal(before, loaded.LastLayer.Xml);
    });

    // A generated view has a class below the user layer; its required reference still resets to that class.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_Apply_AllowsARequiredReferenceResetOnAGeneratedView(bool emptyChoice) => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var view = ContactListView(model);
        var originalClass = ModelEditing.Path(view.ModelClass);
        view.ModelClass = ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestOrder));
        var loaded = build(new StringModelStore(model.LastLayer.Xml));
        var edit = new ModelEditSession();

        if (emptyChoice) edit.SetText(ContactListView(loaded), "ModelClass", "");
        else edit.Reset(ContactListView(loaded), "ModelClass");
        Assert.DoesNotContain("ModelClass", edit.MissingRequired(ContactListView(loaded)));
        edit.Apply(loaded.LastLayer);

        var reloaded = build(new StringModelStore(loaded.LastLayer.Xml));
        Assert.Equal(originalClass, ModelEditing.Path(ContactListView(reloaded).ModelClass));
    });

    // MODELEDITOR-005: a reference value is stored under its persistent name (DetailViewID, IModelListView.cs 63); a saved
    // reset in the warmed-up model takes that attribute out of the user layer.
    [Fact]
    public void Session_ResetOfAReference_TakesItsPersistentValueOutOfTheLayer() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var view = ContactListView(model);
        // A value equal to the calculated default leaves no difference, so a second Contact detail view is chosen.
        var other = (IModelDetailView)ModelEditing.AddChild(((IModelApplication)model).Views, typeof(IModelDetailView), "ModelTestContact_Other_DetailView");
        other.ModelClass = view.ModelClass;
        ModelEditing.SetText(view, "DetailView", "Views/ModelTestContact_Other_DetailView");
        Assert.True(model.LastLayer.Xml.Contains("DetailViewID"), "after set: " + model.LastLayer.Xml);
        // As at runtime: the next circuit loads the saved differences, and the reset is made in that model.
        var loaded = build(new StringModelStore(model.LastLayer.Xml));
        var session = new ModelEditSession();
        session.Reset(ContactListView(loaded), "DetailView");
        session.Apply(loaded.LastLayer);
        Assert.False(loaded.LastLayer.Xml.Contains("DetailViewID"), "after reset: " + loaded.LastLayer.Xml);
    });

    // Codex review: the empty choice of an optional reference is "none", stored as an empty helper value (ModelNode.cs
    // 3115-3126), not a reset, which would bring the calculated DetailView back. Reset takes the "none" out again.
    [Fact]
    public void Session_EmptyChoiceOfAnOptionalReference_StoresNone_AndResetRestoresTheCalculatedValue() => WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var calculated = ModelEditing.Path(ContactListView(model).DetailView);
        Assert.False(ModelEditing.Values(ContactListView(model)).Single(r => r.Name == "DetailView").IsRequired);
        var none = new ModelEditSession();
        none.SetText(ContactListView(model), "DetailView", "");
        none.Apply(model.LastLayer);

        var loaded = build(new StringModelStore(model.LastLayer.Xml));
        Assert.Null(ContactListView(loaded).DetailView);

        var reset = new ModelEditSession();
        reset.Reset(ContactListView(loaded), "DetailView");
        reset.Apply(loaded.LastLayer);
        Assert.Equal(calculated, ModelEditing.Path(ContactListView(build(new StringModelStore(loaded.LastLayer.Xml))).DetailView));
    });

    // MODELEDITOR-007: a stored difference whose node the model no longer has is set aside as unusable when the differences load
    // (ModelNode.CreateMasterNode, docs/api-notes.md DIFF-001), and the database store drops it at the next save, so the editor
    // warns before saving, as the WinForms editor warns after it (ModelEditorViewController.cs 732-737).
    [Fact]
    public void HasUnusableDifferences_ReportsAStoredNodeTheModelNoLongerHas() => WithWarmedUpModels(build => {
        Assert.False(ModelEditing.HasUnusableDifferences(build(ModelStoreBase.Empty)));

        var orphaned = build(new StringModelStore(
            "<Application><Views><DetailView Id=\"NoSuchClass_DetailView\" Caption=\"Gone\" /></Views></Application>"));
        Assert.True(ModelEditing.HasUnusableDifferences(orphaned));
    });

    static IModelListView ContactListView(ModelApplicationBase model) =>
        ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestContact))!.DefaultListView;

    // The running application's steps (ApplicationWarmUpService.RunWarmUpWithModel, AspNetCore/Services/Utils/
    // ApplicationWarmUpService.cs 121-179): the fast lock helper and the calculators cache on, a shared model warmed up;
    // then each application's own model built from the same manager over its user layer (XafApplication.LoadUserDifferences
    // 1485-1519, ApplicationModelsManager.CreateModelApplication 418-429) and collapsed. `build` makes such a model over
    // the given user differences.
    internal static void WithWarmedUpModels(Action<Func<ModelStoreBase, ModelApplicationBase>> test) {
        var optimization = new ApplicationOptions().Optimization;
        var warmUp = optimization.WarmUpApplication;
        var lockHelper = ModelNodeLockHelper.Instance;
        var masterStore = ModelMultipleMasterStore.Instance;
        var calculatorsCache = ModelEditorHelper.ModelCalculatorsCacheEnabled;
        var failFast = XafLayoutBuilderModule.FailFastOnLayoutErrors;
        try {
            optimization.WarmUpApplication = true;
            // The shared values cache is process-wide and WarmUp() fills it only while it is empty (ModelApplication.cs 519-526),
            // so a second warm-up in the same process would use the first model's values. ApplicationWarmUpService.PrepareForWarmUp
            // clears it the same way (AspNetCore/Services/Utils/ApplicationWarmUpService.cs 104-110).
            ModelNodeSharedValuesCache.Instance.Clear();
            DevExpress.ExpressApp.Model.NodeGenerators.ModelNodeGenerationRegistry.ClearNotGeneratedModelNodeDescriptors();
            // Collapse() starts with ModelMultipleMasterStore.Instance, which only BlazorApplication's constructor sets
            // (BlazorApplication.cs 82); without it Collapse throws a NullReferenceException.
            ModelMultipleMasterStore.Instance = new DevExpress.ExpressApp.Blazor.Model.BlazorModelMultipleMasterStore();
            ModelNodeLockHelper.Instance = new DevExpress.ExpressApp.AspNetCore.FastModelNodeLockHelper();
            ModelEditorHelper.ModelCalculatorsCacheEnabled = true;
            // Warming up and collapsing read every node, the broken fixture layouts included; they degrade instead of throwing.
            XafLayoutBuilderModule.FailFastOnLayoutErrors = false;
            var factory = new DesignerModelFactory();
            var module = new ApplicationModelFixture.ModelTestModule { DiffsStore = ModelStoreBase.Empty };
            var manager = factory.CreateApplicationModelManager(module, factory.CreateModulesManager(module, AppContext.BaseDirectory));
            manager.CreateModelApplication([manager.CreateLayer("AfterSetup")]).WarmUp();
            ModelEditorHelper.ModelCalculatorsCacheEnabled = calculatorsCache;

            test(userDifferences => {
                var model = manager.CreateModelApplication([manager.CreateLayerByStore("UserDiff", userDifferences)]);
                model.Collapse();
                return model;
            });
        }
        finally {
            ModelNodeSharedValuesCache.Instance.Clear();
            XafLayoutBuilderModule.FailFastOnLayoutErrors = failFast;
            ModelEditorHelper.ModelCalculatorsCacheEnabled = calculatorsCache;
            ModelNodeLockHelper.Instance = lockHelper;
            ModelMultipleMasterStore.Instance = masterStore;
            optimization.WarmUpApplication = warmUp;
        }
    }
}
