using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-010: the shared (administrator) differences edited in a model of their own over the same warmed-up manager the
// circuit's model uses, saved through the shared store; Reload and the close prompt's bookkeeping in the session.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorSharedModelTests(ApplicationModelFixture fixture) {
    [Fact]
    public void SharedSession_SavesAnEditToTheSharedStore_AndLeavesTheUserModelAlone() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new MemoryDifferenceStore();
        var userModel = manager.CreateModelApplication([manager.CreateLayerByStore("UserDiff", ModelStoreBase.Empty)]);
        userModel.Collapse();
        var original = ContactListView(userModel).Caption;

        using (var shared = new SharedModelSession(manager, store, "")) {
            var session = new ModelEditSession();
            session.SetText(ContactListView(shared.Model), "Caption", "Contacts for everyone");
            session.Apply(shared.Model.LastLayer);
            shared.Save();
        }

        Assert.Contains("Contacts for everyone", store.Xml);
        Assert.Equal(original, ContactListView(userModel).Caption);
        // A circuit built afterwards layers the store below its user layer (XafModelEditorModule) and sees the edit.
        var next = manager.CreateModelApplication([manager.CreateLayerByStore("SharedDiff", store), manager.CreateLayerByStore("UserDiff", ModelStoreBase.Empty)]);
        next.Collapse();
        Assert.Equal("Contacts for everyone", ContactListView(next).Caption);
    });

    [Fact]
    public void SharedSession_LoadsTheStoredSharedDifferences() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new MemoryDifferenceStore {
            Xml = "<Application><Views><ListView Id=\"ModelTestContact_ListView\" Caption=\"Stored shared caption\" /></Views></Application>",
        };
        using var shared = new SharedModelSession(manager, store, "");
        var view = ContactListView(shared.Model);
        Assert.Equal("Stored shared caption", view.Caption);
        Assert.True(ModelEditing.IsModified(view)); // the shared layer is the writable one
    });

    [Fact]
    public void Session_Discard_DropsPendingEditsAndAddedNodes() {
        var view = fixture.Class<ModelTestContact>().DefaultListView;
        var session = new ModelEditSession();
        Assert.False(session.HasPendingEdits);
        session.SetText(view, "Caption", "Pending");
        session.AddChild(view.Columns, typeof(IModelColumn), "DiscardedColumn");
        Assert.True(session.HasPendingEdits);
        session.CloseWarned = true;

        session.Discard();

        Assert.False(session.HasPendingEdits);
        Assert.False(session.CloseWarned);
        Assert.False(session.TryGetPending(view, "Caption", out _, out _));
        Assert.Null(view.Columns["DiscardedColumn"]);
    }

    // Codex review: edits Apply wrote to the model whose save then threw are still unsaved; the session says so until Saved,
    // and Discard reports them so the editor reloads the page instead of pretending the model is as stored.
    [Fact]
    public void Session_AfterAnAppliedButFailedSave_StillHasPendingEdits_AndDiscardReportsThem() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var session = new ModelEditSession();
        session.SetText(ContactListView(model), "Caption", "Applied, not saved");
        session.Apply(model.LastLayer); // the save that follows in the editor threw

        Assert.True(session.HasPendingEdits);
        Assert.False(session.DiscardedApplied);
        Assert.True(session.Discard());
        Assert.False(session.HasPendingEdits);
        Assert.True(session.DiscardedApplied); // the user model must not be stored by XAF's deferred save now

        session.SetText(ContactListView(model), "Caption", "Saved");
        session.Apply(model.LastLayer);
        session.Saved();
        Assert.False(session.HasPendingEdits);
        Assert.False(session.Discard());
    });

    // Codex review 2: a deletion Apply carried out is unsaved too until Saved.
    [Fact]
    public void Session_AfterAnAppliedButFailedDelete_StillHasPendingEdits() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var session = new ModelEditSession();
        var column = session.AddChild(ContactListView(model).Columns, typeof(IModelColumn), "ToDelete");
        session.SetText(column, "PropertyName", nameof(ModelTestContact.Name));
        session.Apply(model.LastLayer);
        session.Saved();
        session.Delete(column);
        session.Apply(model.LastLayer); // the save that follows threw

        Assert.True(session.HasPendingEdits);
        Assert.True(session.Discard());
        Assert.False(session.HasPendingEdits);
    });

    // Codex review 2: the database store refuses a save silently, so the editor compares the record with the layer afterwards.
    [Fact]
    public void StoredHolds_ComparesEveryAspectWithXml_AgainstTheStoredRows() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        model.AddAspect("nl-NL");
        using (ModelEditing.Aspect(model, "nl-NL")) ModelEditing.SetText(ContactListView(model), "Caption", "Contactpersonen");
        ModelEditing.SetText(ContactListView(model), "Caption", "Contacts");
        var layer = model.LastLayer;
        var writer = new ModelXmlWriter();
        var stored = Enumerable.Range(0, layer.AspectCount)
            .Where(i => !string.IsNullOrEmpty(writer.WriteToString(layer, i)))
            .ToDictionary(layer.GetAspect, i => $"{ModelDifferenceDbStore.XafmlHeader}{Environment.NewLine}{writer.WriteToString(layer, i)}");
        Assert.Equal(2, stored.Count);

        Assert.True(SharedModel.StoredHolds(layer, aspect => stored.GetValueOrDefault(aspect)));
        Assert.False(SharedModel.StoredHolds(layer, aspect => aspect == "nl-NL" ? null : stored[aspect])); // a refused new aspect row
        Assert.False(SharedModel.StoredHolds(layer, aspect => stored[aspect].Replace("Contacts", "Old"))); // an older stored version

        // Codex review 3: a reset that emptied the language's aspect; its row is gone or blank after the cleanup, or the save
        // was refused and the row still holds the old caption.
        using (ModelEditing.Aspect(model, "nl-NL")) ModelEditing.Reset(ContactListView(model), "Caption");
        Assert.True(SharedModel.StoredHolds(layer, aspect => aspect == "nl-NL" ? null : stored[aspect]));
        Assert.True(SharedModel.StoredHolds(layer, aspect => aspect == "nl-NL" ? ModelDifferenceDbStore.EmptyXafml : stored[aspect]));
        Assert.False(SharedModel.StoredHolds(layer, aspect => stored.GetValueOrDefault(aspect)));
    });

    // Codex review 3: the store replaces every row, so a save over rows another administrator changed meanwhile is refused.
    [Fact]
    public void RowsChanged_TellsASnapshotFromRowsSavedMeanwhile() {
        var snapshot = new Dictionary<string, string?> { [""] = "<Application/>", ["nl-NL"] = "<Application><Views/></Application>" };
        Assert.False(SharedModel.RowsChanged(snapshot, new Dictionary<string, string?>(snapshot)));
        Assert.True(SharedModel.RowsChanged(snapshot, new Dictionary<string, string?> { [""] = "<Application/>", ["nl-NL"] = "<Application />" }));
        Assert.True(SharedModel.RowsChanged(snapshot, new Dictionary<string, string?> { [""] = "<Application/>" }));
        Assert.True(SharedModel.RowsChanged(snapshot, new Dictionary<string, string?>(snapshot) { ["de-DE"] = "<Application/>" }));
    }

    static IModelListView ContactListView(ModelApplicationBase model) =>
        ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestContact))!.DefaultListView;

    // The default aspect only; the database store writes one row per aspect the same way (ModelDifferenceDbStore.cs 194-213).
    sealed class MemoryDifferenceStore : ModelDifferenceStore {
        public string? Xml { get; set; }

        public override string Name => "Memory";

        public override void Load(ModelApplicationBase model) {
            if (Xml is not null) new ModelXmlReader().ReadFromString(model, "", Xml);
        }

        public override void SaveDifference(ModelApplicationBase model) => Xml = new ModelXmlWriter().WriteToString(model, 0);
    }
}
