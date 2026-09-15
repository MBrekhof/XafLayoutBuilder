using System.Globalization;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-008: localizable values per language (aspect) in the runtime Model Editor, against warmed-up models built
// the way XAF Blazor builds them (WarmedUpModelTests.WithWarmedUpModels). XAF Blazor's warmed-up model takes its current
// aspect from the thread's UI culture (ApplicationWarmUpService.cs 169), and the application's aspect provider changes the
// process-wide default culture when set (CurrentAspectProvider.cs 68-90), so the editor scopes an aspect on the thread
// instead: ModelEditing.Aspect. Both aspect modes are covered.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorLocalizationTests(ApplicationModelFixture fixture) {
    const string Dutch = "nl-NL";

    [Fact]
    public void Aspects_ListTheDefaultFirst_ThenTheLanguages() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect(Dutch);
        var aspects = ModelEditing.Aspects(ContactListView(model));
        Assert.Equal("", aspects[0]);
        Assert.Contains(Dutch, aspects);
    });

    // Codex review: XAF's aspect lookup is case-sensitive (GetAspectIndex, ModelApplication.cs 333-342) while the scope sets the
    // canonical culture, so the name is registered and returned in the culture's own spelling.
    [Fact]
    public void AddAspect_AddsALanguageInItsCanonicalSpelling_AndRefusesAnUnknownCulture() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        Assert.Equal("de-DE", ModelEditing.AddAspect(model, "de-de"));
        Assert.Contains("de-DE", ModelEditing.Aspects(model));
        Assert.DoesNotContain("de-de", ModelEditing.Aspects(model));
        using (ModelEditing.Aspect(model, "de-DE")) Assert.Equal("de-DE", model.CurrentAspect);

        var ex = Assert.ThrowsAny<Exception>(() => ModelEditing.AddAspect(model, "xx-QQ"));
        Assert.Contains("xx-QQ", ex.Message);
        Assert.DoesNotContain("xx-QQ", ModelEditing.Aspects(model));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACaptionSetInAnAspect_ShowsInThatAspectOnly_AndSurvivesAReload(bool aspectFromUICulture) => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        if (aspectFromUICulture) model.UseCurrentUICultureToGetCurrentAspectIndex();
        model.AddAspect(Dutch);
        var view = ContactListView(model);
        var original = view.Caption;

        using (ModelEditing.Aspect(view, Dutch)) ModelEditing.SetText(view, "Caption", "Contactpersonen");

        using (ModelEditing.Aspect(view, Dutch)) {
            Assert.Equal("Contactpersonen", view.Caption);
            Assert.True(Row(view, "Caption").IsModified);
        }
        using (ModelEditing.Aspect(view, "")) {
            Assert.Equal(original, view.Caption);
            Assert.False(Row(view, "Caption").IsModified);
        }
        Assert.Equal(original, view.Caption); // outside any scope: the model's own aspect, the default

        // As at runtime: the next circuit loads every stored aspect (ModelDifferenceDbStore.cs 150).
        var reloaded = build(StoreOf(model.LastLayer));
        Assert.Contains(Dutch, ModelEditing.Aspects(reloaded));
        using (ModelEditing.Aspect(reloaded, Dutch)) Assert.Equal("Contactpersonen", ContactListView(reloaded).Caption);
        using (ModelEditing.Aspect(reloaded, "")) Assert.Equal(original, ContactListView(reloaded).Caption);
        using (ModelEditing.Aspect(reloaded, "en-US")) Assert.Equal(original, ContactListView(reloaded).Caption); // no such aspect: the default
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAspectScope_RestoresTheThreadCulture_AndTheModelsAspect(bool aspectFromUICulture) => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        if (aspectFromUICulture) model.UseCurrentUICultureToGetCurrentAspectIndex();
        model.AddAspect(Dutch);
        var culture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var aspect = model.CurrentAspect;

        using (ModelEditing.Aspect(model, Dutch)) {
            Assert.Equal(Dutch, CultureInfo.CurrentUICulture.Name);
            Assert.Equal(Dutch, model.CurrentAspect);
        }

        Assert.Equal(culture, CultureInfo.CurrentUICulture);
        Assert.Equal(defaultCulture, CultureInfo.DefaultThreadCurrentUICulture); // never the process-wide default
        Assert.Equal(aspect, model.CurrentAspect);
    });

    // MODELEDITOR-010, Codex review 3: XAF's ClearValue drops a localizable value with every language it holds, so a reset in
    // one language must put the others' values back.
    [Fact]
    public void AResetInOneLanguage_KeepsTheOtherLanguagesValues() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect(Dutch);
        var view = ContactListView(model);
        using (ModelEditing.Aspect(view, Dutch)) ModelEditing.SetText(view, "Caption", "Contactpersonen");
        using (ModelEditing.Aspect(view, "")) ModelEditing.SetText(view, "Caption", "Contacts (default)");

        using (ModelEditing.Aspect(view, Dutch)) ModelEditing.Reset(view, "Caption");

        using (ModelEditing.Aspect(view, Dutch)) Assert.False(Row(view, "Caption").IsModified);
        using (ModelEditing.Aspect(view, "")) {
            Assert.True(Row(view, "Caption").IsModified);
            Assert.Equal("Contacts (default)", view.Caption);
        }
        Assert.DoesNotContain("Contactpersonen", new ModelXmlWriter().WriteToString(model.LastLayer, model.GetAspectIndex(Dutch)));
        Assert.Contains("Contacts (default)", model.LastLayer.Xml);
    });

    // The session keeps one pending edit per value and aspect, and Apply writes each in its aspect.
    [Fact]
    public void Session_KeepsAndAppliesEditsOfOneValue_PerAspect() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect(Dutch);
        var view = ContactListView(model);
        var session = new ModelEditSession { Aspect = Dutch };
        session.SetText(view, "Caption", "Contactpersonen");
        session.Aspect = "";
        session.SetText(view, "Caption", "Contacts (default)");

        Assert.True(session.TryGetPending(view, "Caption", out var defaultText, out _));
        Assert.Equal("Contacts (default)", defaultText);
        session.Aspect = Dutch;
        Assert.True(session.TryGetPending(view, "Caption", out var dutchText, out _));
        Assert.Equal("Contactpersonen", dutchText);
        session.Aspect = "";
        session.Apply(model.LastLayer);

        using (ModelEditing.Aspect(view, Dutch)) Assert.Equal("Contactpersonen", view.Caption);
        using (ModelEditing.Aspect(view, "")) Assert.Equal("Contacts (default)", view.Caption);
    });

    // A session without an aspect edits the model's current aspect, as before this card.
    [Fact]
    public void Session_WithoutAnAspect_EditsTheModelsCurrentAspect() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect(Dutch);
        model.SetCurrentAspect(Dutch);
        var view = ContactListView(model);
        var session = new ModelEditSession();
        session.SetText(view, "Caption", "Contactpersonen");
        session.Apply(model.LastLayer);

        Assert.Equal("Contactpersonen", view.Caption);
        using (ModelEditing.Aspect(view, "")) Assert.NotEqual("Contactpersonen", view.Caption);
    });

    // The localization grid: every localizable value with a default-language value under the node, with its translation.
    [Fact]
    public void LocalizableValues_ListTheDefaultAndTheTranslation_AndTellTranslatedRowsApart() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect(Dutch);
        var view = ContactListView(model);
        var original = view.Caption;

        var before = Assert.Single(ModelEditing.LocalizableValues(view, Dutch), r => ReferenceEquals(r.Node, view) && r.Name == "Caption");
        Assert.Equal(original, before.DefaultText);
        Assert.Equal(original, before.TranslatedText);
        Assert.False(before.IsTranslated);
        Assert.All(ModelEditing.LocalizableValues(view, Dutch), r => Assert.NotEqual("", r.DefaultText));
        Assert.Contains(ModelEditing.LocalizableValues(view, Dutch), r => r.Node is IModelColumn); // the subtree, not the node alone

        using (ModelEditing.Aspect(view, Dutch)) ModelEditing.SetText(view, "Caption", "Contactpersonen");

        var after = Assert.Single(ModelEditing.LocalizableValues(view, Dutch), r => ReferenceEquals(r.Node, view) && r.Name == "Caption");
        Assert.Equal(original, after.DefaultText);
        Assert.Equal("Contactpersonen", after.TranslatedText);
        Assert.True(after.IsTranslated);
    });

    // The replay of a saved node's values after Save (ModelEditSession.ReplaySaved) covers every aspect.
    [Fact]
    public void StoredValueWrites_ReplayATranslation() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect(Dutch);
        var view = ContactListView(model);
        using (ModelEditing.Aspect(view, Dutch)) ModelEditing.SetText(view, "Caption", "Contactpersonen");
        var writes = ModelEditing.StoredValueWrites(view);
        using (ModelEditing.Aspect(view, Dutch)) ModelEditing.SetText(view, "Caption", "Overwritten by a view");

        foreach (var (_, write) in writes) write();

        using (ModelEditing.Aspect(view, Dutch)) Assert.Equal("Contactpersonen", view.Caption);
    });

    static ModelValueRow Row(IModelNode node, string name) => ModelEditing.Values(node).Single(r => r.Name == name);

    static IModelListView ContactListView(ModelApplicationBase model) =>
        ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestContact))!.DefaultListView;

    // Every aspect of the layer with differences, the rows the database store writes (ModelDifferenceDbStore.cs 194-213).
    static StringModelStore StoreOf(ModelApplicationBase layer) {
        var store = new StringModelStore();
        var writer = new ModelXmlWriter();
        for (var i = 0; i < layer.AspectCount; i++) {
            var xml = writer.WriteToString(layer, i);
            if (!string.IsNullOrEmpty(xml)) store.Add(layer.GetAspect(i), xml);
        }
        return store;
    }
}
