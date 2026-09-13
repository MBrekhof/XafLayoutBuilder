using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Utils;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-005: drop-downs for reference and type values and suggestions for field names, as the WinForms Model Editor
// offers them (docs/model-editor-scope.md, "Lookups, types, navigation"), against the in-process model.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorLookupTests(ApplicationModelFixture fixture) {
    IModelListView OrderListView => fixture.Class<ModelTestOrder>().DefaultListView;

    static ModelValueRow Row(IModelNode node, string name) => Assert.Single(ModelEditing.Values(node), r => r.Name == name);

    // [DataSourceProperty] lists the views, and [DataSourceCriteria] keeps the ones of the list's class (IModelListView.cs 62-67).
    [Fact]
    public void DetailView_OffersTheDetailViewsOfTheListsClass() {
        var row = Row(OrderListView, "DetailView");
        Assert.True(row.CanEdit);
        Assert.Contains("Views/" + fixture.Class<ModelTestOrder>().DefaultDetailView.Id, row.Choices!);
        Assert.DoesNotContain("Views/" + fixture.Class<ModelTestContact>().DefaultDetailView.Id, row.Choices!);
    }

    [Fact]
    public void SetText_OfAReference_TakesOneOfItsChoices_AndRefusesOtherText() {
        var row = Row(OrderListView, "DetailView");
        var other = row.Choices!.First(c => c != row.Text);
        try {
            ModelEditing.SetText(OrderListView, "DetailView", other);
            Assert.Equal(other, ModelEditing.Path(OrderListView.DetailView));
            Assert.ThrowsAny<Exception>(() => ModelEditing.SetText(OrderListView, "DetailView", "Views/NoSuchView"));
        }
        finally {
            ModelEditing.Reset(OrderListView, "DetailView");
        }
    }

    // A type value with a list ([DataSourceProperty("PropertyEditorTypes")], CommonInterfaces.cs 642-647) is a drop-down too.
    [Fact]
    public void PropertyEditorType_IsADropDown() {
        var row = Row(OrderListView.Columns["Number"]!, "PropertyEditorType");
        Assert.True(row.CanEdit);
        Assert.NotNull(row.Choices);
    }

    // A data source path through another node: "ModelClass.ListEditorsType" is the class node's ListEditorsType list
    // (IModelListView.cs 55-61; the WinForms editor splits the path at its last dot, ModelAttributesPropertyGridHelper.cs 696-705).
    [Fact]
    public void EditorType_OffersTheClassesListEditors() {
        // The in-process model registers no list editors (EditorFactoryLogics.cs 58-70), so the list is the class's, empty here.
        var row = Row(OrderListView, "EditorType");
        Assert.True(row.CanEdit);
        Assert.Equal(OrderListView.ModelClass.ListEditorsType.Select(t => t.FullName!), row.Choices!);
    }

    // Go to: the node a reference value points to; nothing for a text value.
    [Fact]
    public void Referenced_IsTheNodeAReferenceValuePointsTo() {
        Assert.Equal(ModelEditing.Path(OrderListView.DetailView), ModelEditing.Path(ModelEditing.Referenced(OrderListView, "DetailView")!));
        Assert.Null(ModelEditing.Referenced(OrderListView, "Caption"));
    }

    // A lookup's choices can depend on another value of the node (DetailView's criteria on ModelClass), and pending edits are not
    // in the model yet, so a second pending lookup edit on the node waits for a Save (Codex review).
    [Fact]
    public void Session_RefusesASecondPendingLookupEditOnTheSameNode() {
        var detailView = Row(OrderListView, "DetailView");
        var modelClass = Row(OrderListView, "ModelClass");
        var session = new ModelEditSession();
        session.SetText(OrderListView, "DetailView", detailView.Choices!.First(c => c != detailView.Text));

        Assert.Throws<InvalidOperationException>(() => session.SetText(OrderListView, "ModelClass", modelClass.Choices!.First(c => c != modelClass.Text)));
        Assert.False(session.TryGetPending(OrderListView, "ModelClass", out _, out _));
        Assert.False(session.HasErrors);
    }

    // Any pending edit can make a lookup's choices stale (a column's PropertyEditorType depends on its PropertyName), so a lookup
    // edit is the only pending edit until a Save, in either order (Codex review).
    [Fact]
    public void Session_ALookupEditIsTheOnlyPendingEdit() {
        var number = OrderListView.Columns["Number"]!;
        var valueFirst = new ModelEditSession();
        valueFirst.SetText(number, "PropertyName", nameof(ModelTestOrder.Customer));
        Assert.Throws<InvalidOperationException>(() => valueFirst.SetText(number, "PropertyEditorType", ""));

        var detailView = Row(OrderListView, "DetailView");
        var lookupFirst = new ModelEditSession();
        lookupFirst.SetText(OrderListView, "DetailView", detailView.Choices!.First(c => c != detailView.Text));
        Assert.Throws<InvalidOperationException>(() => lookupFirst.SetText(OrderListView, "Caption", "Changed"));
        Assert.Throws<InvalidOperationException>(() => lookupFirst.Delete(OrderListView.Columns["Number"]!));
        Assert.False(lookupFirst.TryGetPending(OrderListView, "Caption", out _, out _));
    }

    // A lookup edit on an added node is written at once, but its choices still come from a model without the pending edits (a
    // view marked for deletion is still offered), so it too is the only edit until a Save, in either order (Codex review 5).
    [Fact]
    public void Session_ALookupEditOnAnAddedNodeIsTheOnlyEdit_InEitherOrder() {
        var views = OrderListView.Application.Views;
        var customerColumn = OrderListView.Columns["Customer"]!;

        var deleteFirst = new ModelEditSession();
        var afterDelete = deleteFirst.AddChild(views, typeof(IModelListView), "LookupAfterDelete_ListView");
        try {
            var orderClass = Row(afterDelete, "ModelClass").Choices!.First(c => c.EndsWith(nameof(ModelTestOrder)));
            deleteFirst.Delete(customerColumn);
            Assert.Throws<InvalidOperationException>(() => deleteFirst.SetText(afterDelete, "ModelClass", orderClass));
        }
        finally {
            deleteFirst.RollbackAdded();
        }

        var lookupFirst = new ModelEditSession();
        var beforeDelete = lookupFirst.AddChild(views, typeof(IModelListView), "LookupBeforeDelete_ListView");
        try {
            lookupFirst.SetText(beforeDelete, "ModelClass", Row(beforeDelete, "ModelClass").Choices!.First(c => c.EndsWith(nameof(ModelTestOrder))));
            Assert.Throws<InvalidOperationException>(() => lookupFirst.Delete(customerColumn));
            Assert.False(lookupFirst.IsPendingDelete(customerColumn));
            lookupFirst.Delete(beforeDelete); // the lookup edit goes with its own node
        }
        finally {
            lookupFirst.RollbackAdded();
        }
    }

    // An added node's Index is written at once, so a move refused by a pending lookup edit must be refused before any sibling is
    // renumbered (Codex review 4).
    [Fact]
    public void Session_AMoveRefusedByAPendingLookupEdit_ChangesNoIndex() {
        var columns = OrderListView.Columns;
        var session = new ModelEditSession();
        var column = session.AddChild(columns, typeof(IModelColumn), "MovedWhileALookupIsPending");
        try {
            session.SetText(column, "PropertyName", nameof(ModelTestOrder.Number));
            session.SetText(column, "Index", ModelEditing.Children(columns).Count(n => n.Index is >= 0).ToString());
            var detailView = Row(OrderListView, "DetailView");
            session.SetText(OrderListView, "DetailView", detailView.Choices!.First(c => c != detailView.Text));
            var before = ModelEditing.Children(columns).ToDictionary(ModelEditing.Id, n => n.Index);

            Assert.Throws<InvalidOperationException>(() => session.Move(column, up: true));
            Assert.Equal(before, ModelEditing.Children(columns).ToDictionary(ModelEditing.Id, n => n.Index));
        }
        finally {
            session.RollbackAdded();
        }
    }

    // Go to follows the value the editor shows: a pending selection, not the model's value yet (Codex review).
    [Fact]
    public void Session_Referenced_FollowsAPendingSelection() {
        var row = Row(OrderListView, "DetailView");
        var other = row.Choices!.First(c => c != row.Text);
        var session = new ModelEditSession();
        session.SetText(OrderListView, "DetailView", other);
        Assert.Equal(other, ModelEditing.Path(session.Referenced(OrderListView, "DetailView")!));
    }

    // Source: [ModelValueCalculator("ModelClass", "DefaultDetailView")] on IModelListView.DetailView (IModelListView.cs 62).
    [Fact]
    public void ValueSource_OfAListViewsDetailView_IsItsClasssDefaultDetailView() {
        var source = ModelEditing.ValueSource(OrderListView, "DetailView")!.Value;
        Assert.Equal(ModelEditing.Path(OrderListView.ModelClass), ModelEditing.Path(source.Node));
        Assert.Equal("DefaultDetailView", source.Name);
    }

    // A calculator's node name can be "this" or a path from the root (IModelListView.cs 68, IModelView.cs 64) (Codex review).
    [Fact]
    public void ValueSource_FollowsThisAndPathsFromTheRoot() {
        var own = ModelEditing.ValueSource(OrderListView, "MasterDetailView")!.Value;
        Assert.Equal((ModelEditing.Path(OrderListView), "DetailView"), (ModelEditing.Path(own.Node), own.Name));
        var options = ModelEditing.ValueSource(OrderListView, "CustomizationFormEnabled")!.Value;
        Assert.Equal((ModelEditing.Path(OrderListView.Application.Options), "CustomizationFormEnabled"), (ModelEditing.Path(options.Node), options.Name));
    }

    // The WinForms field picker for PropertyName lists the members of the view's class (ModelAttributesPropertyGridHelper.cs
    // 405-421); here they are suggestions, since a property path such as Customer.Name is allowed too.
    [Fact]
    public void PropertyName_SuggestsTheMembersOfTheViewsClass() {
        var row = Row(OrderListView.Columns["Number"]!, "PropertyName");
        Assert.Contains(nameof(ModelTestOrder.Customer), row.Suggestions!);
    }

    // As the WinForms combo: the default and the user language, then the model's languages (ModelAttributesPropertyGridHelper.cs 850-856).
    [Fact]
    public void PreferredLanguage_SuggestsTheDefaultAndTheUserLanguage() {
        var row = Row(OrderListView.Application, "PreferredLanguage");
        Assert.Equal([CaptionHelper.DefaultLanguage, CaptionHelper.UserLanguage], row.Suggestions!.Take(2));
    }
}
