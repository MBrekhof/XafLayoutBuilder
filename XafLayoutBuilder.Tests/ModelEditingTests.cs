using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-001: the runtime Model Editor's node and value logic against the in-process Application Model. Every edit
// is taken back in a finally block: the model is shared by the whole collection.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditingTests(ApplicationModelFixture fixture) {
    IModelListView View => fixture.Class<ModelTestContact>().DefaultListView;

    [Fact]
    public void Children_AndPath_NameTheNodesBelowTheRoot() {
        var views = Assert.Single(ModelEditing.Children(fixture.Model), n => ModelEditing.Path(n) == "Views");
        Assert.Contains(ModelEditing.Children(views), n => ModelEditing.Path(n) == "Views/ModelTestContact_ListView");
    }

    [Fact]
    public void Values_OfferTheViewsCaption_ButNotTheNodeBookkeeping() {
        var rows = ModelEditing.Values(View);
        Assert.True(Assert.Single(rows, r => r.Name == "Caption").CanEdit);
        Assert.DoesNotContain(rows, r => r.Name is "Id" or "IsNewNode" or "IsRemovedNode");
    }

    [Fact]
    public void SetText_WritesTheWritableLayer_AndResetTakesItBack() {
        var original = View.Caption;
        try {
            ModelEditing.SetText(View, "Caption", "Edited at runtime");
            Assert.Equal("Edited at runtime", View.Caption);
            Assert.True(Row("Caption").IsModified);

            ModelEditing.Reset(View, "Caption");
            Assert.Equal(original, View.Caption);
            Assert.False(Row("Caption").IsModified);
        }
        finally {
            ModelEditing.Reset(View, "Caption");
        }
    }

    [Fact]
    public void SetText_ConvertsEnumsAndBooleans_AndOffersTheirChoices() {
        try {
            ModelEditing.SetText(View, "MasterDetailMode", nameof(MasterDetailMode.ListViewAndDetailView));
            ModelEditing.SetText(View, "AllowEdit", "True");
            Assert.Equal(MasterDetailMode.ListViewAndDetailView, View.MasterDetailMode);
            Assert.True(View.AllowEdit);
            Assert.Equal(Enum.GetNames<MasterDetailMode>(), Row("MasterDetailMode").Choices);
            Assert.Equal(["True", "False"], Row("AllowEdit").Choices);
        }
        finally {
            ModelEditing.Reset(View, "MasterDetailMode");
            ModelEditing.Reset(View, "AllowEdit");
        }
    }

    // Codex review: an edit written to the running model before Save would be persisted by any later model save. The session
    // keeps edits pending and writes them only on Apply (Save); closing the editor simply drops them. Rolling back instead
    // does not work in XAF Blazor: its warmed-up model caches values, and ClearValue does not update that cache.
    [Fact]
    public void PendingEdits_LeaveTheModelAlone_UntilApply() {
        var original = View.Caption;
        try {
            var session = new ModelEditSession();
            session.SetText(View, "Caption", "Pending");
            Assert.Equal(original, View.Caption);
            Assert.True(session.TryGetPending(View, "Caption", out var pending, out var error));
            Assert.Equal("Pending", pending);
            Assert.Null(error);

            session.Apply();
            Assert.Equal("Pending", View.Caption);
            Assert.False(session.TryGetPending(View, "Caption", out _, out _));
        }
        finally {
            ModelEditing.Reset(View, "Caption");
        }
    }

    [Fact]
    public void PendingReset_ClearsAValueTheWritableLayerHeld_OnApply() {
        try {
            ModelEditing.SetText(View, "Caption", "Saved earlier");
            var session = new ModelEditSession();
            session.Reset(View, "Caption");
            Assert.Equal("Saved earlier", View.Caption);
            Assert.True(session.TryGetPending(View, "Caption", out var pending, out _));
            Assert.Null(pending);

            session.Apply();
            Assert.False(Row("Caption").IsModified);
        }
        finally {
            ModelEditing.Reset(View, "Caption");
        }
    }

    // Codex re-review: text that is no value of its type must supersede an earlier valid edit of the same value and block
    // Save, not leave that earlier edit to be saved under a "Saved" message.
    [Fact]
    public void InvalidText_SupersedesAnEarlierEdit_AndBlocksApplyUntilCorrected() {
        var original = View.TopReturnedObjects;
        try {
            var session = new ModelEditSession();
            session.SetText(View, "TopReturnedObjects", "30");
            Assert.ThrowsAny<Exception>(() => session.SetText(View, "TopReturnedObjects", "twenty"));
            Assert.True(session.TryGetPending(View, "TopReturnedObjects", out var pending, out var error));
            Assert.Equal("twenty", pending);
            Assert.NotNull(error);
            Assert.True(session.HasErrors);

            Assert.Throws<InvalidOperationException>(session.Apply);
            Assert.Equal(original, View.TopReturnedObjects);

            session.SetText(View, "TopReturnedObjects", "25");
            Assert.False(session.HasErrors);
            session.Apply();
            Assert.Equal(25, View.TopReturnedObjects);
        }
        finally {
            ModelEditing.Reset(View, "TopReturnedObjects");
        }
    }

    ModelValueRow Row(string name) => ModelEditing.Values(View).Single(r => r.Name == name);
}
