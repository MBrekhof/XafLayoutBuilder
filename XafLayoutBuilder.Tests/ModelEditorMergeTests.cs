using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-009: the selected node's differences as XML, Merge Differences from the user layer into the shared
// differences (the XML the stores themselves read and write, since ModelNode.MoveNodeToOtherLayer is internal and refuses a
// layer of another model), and Generate Content on a node added in the editor. Warmed-up models, as XAF Blazor runs them.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorMergeTests(ApplicationModelFixture fixture) {
    const string ViewId = "ModelTestContact_ListView";

    [Fact]
    public void DifferencesXml_ShowsTheNodesDifferencesPerAspect() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var view = ContactListView(model);
        Assert.Empty(ModelEditing.DifferencesXml(model, view));

        view.Caption = "Contacts";
        using (ModelEditing.Aspect(view, ModelEditing.AddAspect(view, "nl-NL"))) view.Caption = "Contacten";

        var xml = ModelEditing.DifferencesXml(model, view);
        Assert.Equal(["", "nl-NL"], xml.Select(x => x.Aspect));
        Assert.Contains("Caption=\"Contacts\"", xml[0].Xml);
        Assert.Contains("Caption=\"Contacten\"", xml[1].Xml);
        Assert.Empty(ModelEditing.DifferencesXml(model, view.Columns["Phone"]!)); // a node the layer does not hold
    });

    [Fact]
    public void DifferencesForMerge_HoldsOnlyThePathToTheNode() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var view = ContactListView(model);
        view.Caption = "Own caption";
        view.Columns["Phone"]!.Width = 123;
        view.Columns["Email"]!.Width = 77;

        var xml = Assert.Single(ModelEditing.DifferencesForMerge(model, view.Columns["Phone"]!).Xml).Xml;

        Assert.Contains($"<ListView Id=\"{ViewId}\">", xml); // the ancestor with its key only
        Assert.Contains("Width=\"123\"", xml);
        Assert.DoesNotContain("Own caption", xml);
        Assert.DoesNotContain("77", xml);
    });

    [Fact]
    public void Merge_MovesValuesIntoTheSharedStore_PerAspect_AndKeepsWhatTheSharedStoreHeld() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new AspectStore();
        store.Xml[""] = $"<Application><Views><ListView Id=\"{ViewId}\" Caption=\"Shared caption\"><Columns><ColumnInfo Id=\"Phone\" Caption=\"Shared phone\" /></Columns></ListView></Views></Application>";
        var userModel = Circuit(manager, store);
        var phone = ContactListView(userModel).Columns["Phone"]!;
        phone.Width = 123;
        phone.Caption = "Telephone";
        using (ModelEditing.Aspect(phone, ModelEditing.AddAspect(phone, "nl-NL"))) phone.Caption = "Telefoon";

        // Measured here: with the shared layer holding the node too, the merged node reports no modification and its Undo does
        // nothing, so the editor asks the user layer's own node.
        Assert.False(ModelEditing.IsModified(phone));
        Assert.True(ModelEditing.IsModified(userModel, phone));

        Merge(manager, store, userModel, phone);

        Assert.Contains("Width=\"123\"", store.Xml[""]);
        Assert.Contains("Caption=\"Telephone\"", store.Xml[""]);
        Assert.DoesNotContain("Shared phone", store.Xml[""]);
        Assert.Contains("Shared caption", store.Xml[""]);
        Assert.Contains("Caption=\"Telefoon\"", store.Xml["nl-NL"]);

        // The user side: the node's differences leave the user layer.
        var session = new ModelEditSession { Model = userModel };
        session.ResetNode(phone);
        session.Apply(userModel.LastLayer);
        Assert.Empty(ModelEditing.DifferencesXml(userModel, phone));
        Assert.False(ModelEditing.IsModified(userModel, phone));

        var next = ContactListView(Circuit(manager, store)).Columns["Phone"]!;
        Assert.Equal(123, next.Width);
        Assert.Equal("Telephone", next.Caption);
        using (ModelEditing.Aspect(next, "nl-NL")) Assert.Equal("Telefoon", next.Caption);
    });

    [Fact]
    public void Merge_OfAnAddedNode_MakesItTheSharedModels_AndDeleteTakesItOutOfTheUserLayer() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new AspectStore();
        var userModel = Circuit(manager, store);
        var columns = ContactListView(userModel).Columns;
        var added = new ModelEditSession();
        var extra = (IModelColumn)added.AddChild(columns, typeof(IModelColumn), "Extra");
        added.SetText(extra, "PropertyName", "Email");
        added.Apply(userModel.LastLayer);
        added.Saved();

        Merge(manager, store, userModel, extra);
        Assert.Contains("Id=\"Extra\"", store.Xml[""]);
        Assert.Contains("IsNewNode=\"True\"", store.Xml[""]);

        var session = new ModelEditSession();
        session.Delete(extra);
        session.Apply(userModel.LastLayer);
        Assert.DoesNotContain("Extra", new ModelXmlWriter().WriteToString(userModel.LastLayer, 0)); // removed, no tombstone

        Assert.Equal("Email", ContactListView(Circuit(manager, store)).Columns["Extra"]!.PropertyName);
    });

    [Fact]
    public void Merge_OfARemovedGeneratedNode_RemovesItForEveryone() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new AspectStore();
        var userModel = Circuit(manager, store);
        var columns = ContactListView(userModel).Columns;
        columns["Email"]!.Remove();

        Merge(manager, store, userModel, columns);

        Assert.Contains("<ColumnInfo Id=\"Email\" Removed=\"True\" />", store.Xml[""]);
        Assert.Null(ContactListView(Circuit(manager, store)).Columns["Email"]);
    });

    [Fact]
    public void Merge_UnderANodeTheUserAdded_IsRefused_NamingThatNode() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var columns = ContactListView(model).Columns;
        var extra = columns.AddNode<IModelColumn>("Extra");
        extra.PropertyName = "Email";
        var summary = extra.Summary.AddNode<IModelColumnSummaryItem>("Count");

        var refusal = Assert.Throws<InvalidOperationException>(() => ModelEditing.DifferencesForMerge(model, summary));
        Assert.Contains($"Views/{ViewId}/Columns/Extra", refusal.Message);
    });

    // Codex plan review: reading XML over the shared layer is no structural move. A replaced node (deleted and added again
    // under the same id) and a deletion of a node the shared layer itself holds are refused; Edit Shared Model does those.
    [Fact]
    public void Merge_OfAReplacedNode_IsRefused() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var columns = ContactListView(model).Columns;
        columns["Phone"]!.Remove();
        columns.AddNode<IModelColumn>("Phone").PropertyName = "Phone";

        Assert.Throws<InvalidOperationException>(() => ModelEditing.DifferencesForMerge(model, columns));
    });

    [Fact]
    public void Merge_OfADeletionOfANodeTheSharedLayerHolds_IsRefused_AndSavesNothing() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new AspectStore();
        var stored = store.Xml[""] = $"<Application><Views><ListView Id=\"{ViewId}\"><Columns><ColumnInfo Id=\"SharedExtra\" PropertyName=\"Email\" IsNewNode=\"True\" /></Columns></ListView></Views></Application>";
        var userModel = Circuit(manager, store);
        var columns = ContactListView(userModel).Columns;
        columns["SharedExtra"]!.Remove();

        Assert.Throws<InvalidOperationException>(() => Merge(manager, store, userModel, columns));
        Assert.Equal(stored, store.Xml[""]);
    });

    // Codex plan review 2: a node the user added that the model now has of its own too (a module's, or here the shared
    // layer's): deleting the user's copy would leave a tombstone hiding the merged node (ModelNode.CanRemoveNode, 737-761).
    [Fact]
    public void Merge_OfAnAddedNodeTheModelAlsoHas_IsRefused() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        const string extra = "<ColumnInfo Id=\"Extra\" PropertyName=\"Email\" IsNewNode=\"True\" />";
        var xml = $"<Application><Views><ListView Id=\"{ViewId}\"><Columns>{extra}</Columns></ListView></Views></Application>";
        var store = new AspectStore();
        store.Xml[""] = xml;
        var user = new StringModelStore();
        user.Add("", xml);
        var userModel = manager.CreateModelApplication([manager.CreateLayerByStore("SharedDiff", store), manager.CreateLayerByStore("UserDiff", user)]);
        userModel.Collapse();

        var refusal = Assert.Throws<InvalidOperationException>(() => ModelEditing.DifferencesForMerge(userModel, ContactListView(userModel).Columns["Extra"]!));
        Assert.Contains("shadows", refusal.Message);
    });

    // Codex plan review 2: the node a difference is for may be gone from the shared model by now; the editor asks the session
    // before it saves.
    [Fact]
    public void SharedSession_HasNode_TellsWhetherTheMergedModelHoldsThePath() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        using var shared = new SharedModelSession(manager, new AspectStore(), "");
        Assert.True(shared.HasNode(["Views", ViewId, "Columns", "Phone"]));
        Assert.False(shared.HasNode(["Views", "GoneSince_ListView"]));
    });

    // Codex diff review: the node a difference is for may be gone further down too. A column the shared record held when the
    // user's circuit was built, deleted from it since: its override would be dropped as unusable by the shared model and then
    // reset on the user side, lost from both.
    [Fact]
    public void SharedSession_FirstMissing_NamesADescendantTheSharedModelLostSince() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var store = new AspectStore();
        store.Xml[""] = $"<Application><Views><ListView Id=\"{ViewId}\"><Columns><ColumnInfo Id=\"SharedExtra\" PropertyName=\"Email\" IsNewNode=\"True\" /></Columns></ListView></Views></Application>";
        var userModel = Circuit(manager, store);
        var columns = ContactListView(userModel).Columns;
        columns["SharedExtra"]!.Width = 55;
        store.Xml.Clear(); // another administrator deleted the column

        var differences = ModelEditing.DifferencesForMerge(userModel, columns);
        using var shared = new SharedModelSession(manager, store, "", preload: layer => ModelEditing.MergeInto(layer, differences));

        Assert.True(shared.HasNode(differences.Ids)); // the merged node itself is there
        Assert.Equal($"Views/{ViewId}/Columns/SharedExtra", shared.FirstMissing(differences.Nodes));
    });

    // Codex re-review: a deleted node keeps its differences underneath (ModelNode._Delete, 771-791, only sets the flags), and
    // the merged model has none of them; they are not asked of it.
    [Fact]
    public void DifferencesForMerge_DoesNotExpectNodesUnderADeletedNode() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var view = ContactListView(model);
        view.Columns["Phone"]!.Width = 5;
        var views = view.Application.Views;
        view.Remove();

        var nodes = ModelEditing.DifferencesForMerge(model, views).Nodes.Select(ids => string.Join("/", ids)).ToList();

        Assert.Equal(["Views"], nodes);
    });

    // MODELEDITOR-015: a merge whose user-side save was refused leaves the user's own copy of an added node over the shared
    // one. Merge refuses it, Reset node is not offered for a node the user added, and Delete would write a tombstone that
    // hides the shared node: Drop my copy empties the user layer's own node, which the writer then leaves out altogether.
    [Fact]
    public void CanDropCopy_OnlyForTheUsersOwnCopyOfANodeAnotherLayerHoldsToo() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var model = Layered(manager, AddedColumn("Extra"), AddedColumn("Extra"));
        var columns = ContactListView(model).Columns;
        var session = new ModelEditSession { Model = model };

        Assert.True(session.CanDropCopy(columns["Extra"]!));
        Assert.False(session.CanDropCopy(columns["Phone"]!)); // generated, not the user's own: Reset node

        var onlyMine = Layered(manager, null, AddedColumn("Extra"));
        var mine = ContactListView(onlyMine).Columns["Extra"]!;
        Assert.False(new ModelEditSession { Model = onlyMine }.CanDropCopy(mine)); // nothing below it: Delete is the answer

        var adding = new ModelEditSession { Model = model };
        var justAdded = adding.AddChild(columns, typeof(IModelColumn), "AddedNow");
        Assert.False(adding.CanDropCopy(justAdded)); // added in this session: Delete removes it outright
    });

    [Fact]
    public void DropCopy_TakesTheNodeOutOfTheUserLayer_AndTheSharedOneShowsAgain() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var shared = AddedColumn("Extra", width: 42);
        var model = Layered(manager, shared, AddedColumn("Extra", width: 7));
        var session = new ModelEditSession { Model = model };

        session.DropCopy(ContactListView(model).Columns["Extra"]!);
        Assert.True(session.HasPendingEdits);
        session.Apply(model.LastLayer);

        var userXml = new ModelXmlWriter().WriteToString(model.LastLayer, 0);
        Assert.DoesNotContain("Extra", userXml);
        Assert.DoesNotContain("Removed", userXml);
        var next = ContactListView(Layered(manager, shared, userXml.Length == 0 ? null : userXml)).Columns["Extra"]!;
        Assert.Equal(42, next.Width); // the shared column, not the user's own
    });

    // Why Drop my copy exists: Delete on the same node writes a tombstone, and the shared node goes with it.
    [Fact]
    public void Delete_OnTheUsersOwnCopy_HidesTheSharedNodeToo() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var shared = AddedColumn("Extra");
        var model = Layered(manager, shared, AddedColumn("Extra"));
        var session = new ModelEditSession { Model = model };

        session.Delete(ContactListView(model).Columns["Extra"]!);
        session.Apply(model.LastLayer);

        var userXml = new ModelXmlWriter().WriteToString(model.LastLayer, 0);
        Assert.Contains("Removed", userXml);
        Assert.Null(ContactListView(Layered(manager, shared, userXml)).Columns["Extra"]);
    });

    // Why Reset node is not the answer either: it is offered here (CanResetNode asks the merged node, which carries no mark of
    // its own), but Undo keeps the layer node's IsNewNode (Codex, MODELEDITOR-009), so the user's record keeps an empty node
    // of its own. Drop my copy takes it out.
    [Fact]
    public void ResetNode_OnTheUsersOwnCopy_LeavesAnEmptyNodeOfItsOwnBehind() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var model = Layered(manager, AddedColumn("Extra"), AddedColumn("Extra", width: 7));
        var session = new ModelEditSession { Model = model };
        Assert.True(session.CanResetNode(ContactListView(model).Columns["Extra"]!));

        session.ResetNode(ContactListView(model).Columns["Extra"]!);
        session.Apply(model.LastLayer);

        var userXml = new ModelXmlWriter().WriteToString(model.LastLayer, 0);
        Assert.DoesNotContain("Width", userXml); // the values are gone
        Assert.Contains("Extra", userXml);       // the node itself is not
    });

    // Codex plan review: a lower layer whose node is itself a deletion. Measured: the node counts as held by one layer then,
    // so Drop my copy is not offered; there is nothing below to show again, and Delete is the answer.
    [Fact]
    public void DropCopy_IsNotOfferedWhenTheLayerBelowOnlyDeletesTheNode() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var shared = $"<Application><Views><ListView Id=\"{ViewId}\"><Columns><ColumnInfo Id=\"Phone\" Removed=\"True\" /></Columns></ListView></Views></Application>";
        var model = Layered(manager, shared, AddedColumn("Phone"));
        var session = new ModelEditSession { Model = model };

        Assert.False(session.CanDropCopy(ContactListView(model).Columns["Phone"]!));
        Assert.Throws<InvalidOperationException>(() => session.DropCopy(ContactListView(model).Columns["Phone"]!));
    });

    // Codex diff review: a pending drop is a change to the saved model like a node reset, so the edits that are refused
    // beside a pending reset are refused beside it too.
    [Fact]
    public void WhileADropIsPending_ALookupEditAndACloneOfThatNodeAreRefused() => WarmedUpModelTests.WithWarmedUpManager(manager => {
        var model = Layered(manager, AddedColumn("Extra"), AddedColumn("Extra", width: 7));
        var view = ContactListView(model);
        var session = new ModelEditSession { Model = model };
        session.DropCopy(view.Columns["Extra"]!);

        // A lookup's choices are worked out from the saved model, which the drop changes.
        Assert.Throws<InvalidOperationException>(() => session.SetText(view, "DetailView", "Views/ModelTestContact_DetailView"));
        // A clone would copy the values the drop takes away.
        Assert.Throws<InvalidOperationException>(() => session.Clone(view.Columns["Extra"]!, "ExtraCopy"));
        Assert.Null(view.Columns["ExtraCopy"]);
    });

    static string AddedColumn(string id, int width = 42) =>
        $"<Application><Views><ListView Id=\"{ViewId}\"><Columns><ColumnInfo Id=\"{id}\" PropertyName=\"Email\" IsNewNode=\"True\" Width=\"{width}\" /></Columns></ListView></Views></Application>";

    // A circuit's model over the shared differences and the user's own, both given as XML.
    static ModelApplicationBase Layered(ApplicationModelManager manager, string? sharedXml, string? userXml) {
        var shared = new StringModelStore();
        if (sharedXml is not null) shared.Add("", sharedXml);
        var user = new StringModelStore();
        if (userXml is not null) user.Add("", userXml);
        var model = manager.CreateModelApplication([manager.CreateLayerByStore("SharedDiff", shared), manager.CreateLayerByStore("UserDiff", user)]);
        model.Collapse();
        return model;
    }

    [Fact]
    public void GenerateContent_IsOfferedForViews_NotForColumns() {
        var view = fixture.Class<ModelTestContact>().DefaultListView;
        Assert.True(ModelEditing.IsGenerateContentNode(view));
        Assert.False(ModelEditing.IsGenerateContentNode(view.Columns["Phone"]!));
        Assert.False(new ModelEditSession().CanGenerateContent(view)); // a node of this session only
    }

    [Fact]
    public void GenerateContent_FillsAnAddedListViewsColumns_OnceItsClassIsSet() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var session = new ModelEditSession();
        var view = (IModelListView)session.AddChild(((IModelApplication)model).Views, typeof(IModelListView), "Contacts_Custom");
        Assert.True(session.CanGenerateContent(view));
        Assert.Throws<InvalidOperationException>(() => session.GenerateContent(view)); // ModelClass is required first

        var modelClass = ModelEditing.Values(view).Single(r => r.Name == "ModelClass").Choices!.Single(c => c.EndsWith(nameof(ModelTestContact), StringComparison.Ordinal));
        session.SetText(view, "ModelClass", modelClass);
        var siblings = ((IModelApplication)model).Views.Count;
        session.GenerateContent(view);

        Assert.Equal(["Email", "Name", "Phone"], view.Columns.Select(c => c.PropertyName).Order());
        Assert.Equal(siblings, ((IModelApplication)model).Views.Count); // the helper's temporary sibling is gone
    });

    [Fact]
    public void GenerateContent_IsRefused_WhileEditsArePendingElsewhere() => WarmedUpModelTests.WithWarmedUpModels(build => {
        var model = build(ModelStoreBase.Empty);
        var session = new ModelEditSession();
        var view = (IModelListView)session.AddChild(((IModelApplication)model).Views, typeof(IModelListView), "Contacts_Custom");
        session.SetText(ContactListView(model), "Caption", "Pending");

        Assert.Throws<InvalidOperationException>(() => session.GenerateContent(view));
    });

    // A circuit's model as XafModelEditorModule builds it: the shared store's layer below the user layer.
    static ModelApplicationBase Circuit(ApplicationModelManager manager, ModelDifferenceStore shared) {
        var model = manager.CreateModelApplication([manager.CreateLayerByStore("SharedDiff", shared), manager.CreateLayerByStore("UserDiff", ModelStoreBase.Empty)]);
        model.Collapse();
        return model;
    }

    // The shared half of the editor's Merge: the node's differences read into the shared layer before it joins its model.
    static void Merge(ApplicationModelManager manager, ModelDifferenceStore store, ModelApplicationBase userModel, IModelNode node) {
        var differences = ModelEditing.DifferencesForMerge(userModel, node);
        using var shared = new SharedModelSession(manager, store, "", preload: layer => ModelEditing.MergeInto(layer, differences));
        shared.Save();
    }

    static IModelListView ContactListView(ModelApplicationBase model) =>
        ((IModelApplication)model).BOModel.GetClass(typeof(ModelTestContact))!.DefaultListView;

    // One XML per aspect, as the database store keeps one row per aspect (ModelDifferenceDbStore.cs 194-213).
    sealed class AspectStore : ModelDifferenceStore {
        public Dictionary<string, string> Xml { get; } = [];

        public override string Name => "Memory";

        public override void Load(ModelApplicationBase model) {
            foreach (var (aspect, xml) in Xml) new ModelXmlReader().ReadFromString(model, aspect, xml);
        }

        public override void SaveDifference(ModelApplicationBase model) {
            Xml.Clear();
            var writer = new ModelXmlWriter();
            for (var i = 0; i < model.AspectCount; i++) {
                if (writer.WriteToString(model, i) is { Length: > 0 } xml) Xml[model.GetAspect(i)] = xml;
            }
        }
    }
}
