using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-014: a user's value on a node the shared layer holds too. There ModelNode.GetWritableLayer misses the user
// layer, so IsValueModified is false and ClearValue does nothing on the merged node (measured; docs/api-notes.md); the editor
// asks the user layer's own node, which needs the model.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorLayeredValueTests(ApplicationModelFixture fixture) {
    const string SharedXml = "<Application><Views><ListView Id=\"ModelTestContact_ListView\"><Columns><ColumnInfo Id=\"Phone\" Caption=\"Shared phone\" /></Columns></ListView></Views></Application>";

    [Fact]
    public void Values_MarkAUsersValueModified_OnANodeTheSharedLayerHoldsToo() => WithLayeredModel((model, phone) => {
        phone.Width = 123;

        Assert.False(ModelEditing.Values(phone).Single(r => r.Name == "Width").IsModified); // DevExpress's own answer
        Assert.True(ModelEditing.Values(phone, model).Single(r => r.Name == "Width").IsModified);
        Assert.False(ModelEditing.Values(phone, model).Single(r => r.Name == "Index").IsModified); // a value the user did not touch
    });

    [Fact]
    public void Session_Reset_TakesTheValueOutOfTheUserLayer() => WithLayeredModel((model, phone) => {
        phone.Width = 123;
        var session = new ModelEditSession { Model = model };

        session.Reset(phone, "Width");
        session.Apply(model.LastLayer);

        Assert.DoesNotContain("Width=", UserXml(model));
    });

    [Fact]
    public void Session_EmptyText_ClearsTheValueInTheUserLayer() => WithLayeredModel((model, phone) => {
        phone.Width = 123;
        var session = new ModelEditSession { Model = model };

        session.SetText(phone, "Width", "");
        session.Apply(model.LastLayer);

        Assert.DoesNotContain("Width=", UserXml(model));
    });

    // The MODELEDITOR-010 write-back (ClearValue drops every language of a value) in this configuration.
    [Fact]
    public void Session_ResetInOneLanguage_KeepsTheDefaultLanguagesValue() => WithLayeredModel((model, phone) => {
        phone.Caption = "Telephone";
        using (ModelEditing.Aspect(phone, ModelEditing.AddAspect(phone, "nl-NL"))) phone.Caption = "Telefoon";
        var session = new ModelEditSession { Model = model, Aspect = "nl-NL" };

        session.Reset(phone, "Caption");
        session.Apply(model.LastLayer);

        Assert.Contains("Caption=\"Telephone\"", UserXml(model));
        Assert.DoesNotContain("Telefoon", string.Join("", ModelEditing.DifferencesXml(model, phone).Select(x => x.Xml)));
    });

    // Codex diff review: the application root has no ids, and is the layer itself.
    [Fact]
    public void Values_OfTheApplicationRoot_AreAskedOfTheLayerItself() => WithLayeredModel((model, _) => {
        ((IModelApplication)model).Title = "My title";

        Assert.True(ModelEditing.Values(model, model).Single(r => r.Name == "Title").IsModified);
    });

    // Codex plan review: the replay clears a value written in a language the snapshot held nothing for, and ClearValue drops
    // every language of a value (ModelNode.cs 2384-2390; measured: a clear in nl-NL took the default caption out of the XML).
    // Asserted on the XML: the warmed-up cache would still answer with the old value.
    [Fact]
    public void StoredValueWrites_ReplayKeepsTheDefaultLanguage_WhenItClearsATranslationWrittenSince() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var columns = ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestContact))!.DefaultListView.Columns;
        var extra = columns.AddNode<IModelColumn>("ReplayExtra");
        extra.PropertyName = "Email";
        extra.Caption = "Extra caption";
        ModelEditing.AddAspect(extra, "nl-NL");
        var writes = ModelEditing.StoredValueWrites(extra, model);
        using (ModelEditing.Aspect(extra, "nl-NL")) extra.Caption = "Sindsdien vertaald";

        foreach (var (_, write) in writes) write();

        Assert.Contains("Caption=\"Extra caption\"", UserXml(model));
        Assert.DoesNotContain("Sindsdien vertaald", string.Join("", ModelEditing.DifferencesXml(model, extra).Select(x => x.Xml)));
    });

    static string UserXml(ModelApplicationBase model) => new ModelXmlWriter().WriteToString(model.LastLayer, 0);

    void WithLayeredModel(Action<ModelApplicationBase, IModelColumn> test) => WarmedUpModelTests.WithWarmedUpManager(manager => {
        _ = fixture;
        var shared = new StringModelStore();
        shared.Add("", SharedXml);
        var model = manager.CreateModelApplication([manager.CreateLayerByStore("SharedDiff", shared), manager.CreateLayerByStore("UserDiff", ModelStoreBase.Empty)]);
        model.Collapse();
        test(model, ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestContact))!.DefaultListView.Columns["Phone"]!);
    });
}
