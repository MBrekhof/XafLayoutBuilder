using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-003: the tree and value grid the WinForms Model Editor shows (docs/model-editor-scope.md, "Tree" and
// "Property grid"), against the in-process model. Edits are taken back in a finally block: the model is shared.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorTreeTests(ApplicationModelFixture fixture) {
    IModelListView OrderListView => fixture.Class<ModelTestOrder>().DefaultListView;

    // The WinForms tree's order (ModelTreeListNodeComparer over ModelNodeComparerBase): shown columns by Index, then the
    // columns with a negative Index.
    [Fact]
    public void Children_ListShownColumnsInIndexOrder_BeforeHiddenOnes() {
        var ids = ModelEditing.Children(OrderListView.Columns).Select(ModelEditing.Id).ToList();
        Assert.Equal(["Number", "Customer", "OrderDate"], ids.Take(3));
        Assert.True(ids.IndexOf(nameof(ModelTestOrder.SyncToken)) > ids.IndexOf("OrderDate"));
    }

    // The expand button follows the node type (a column can have summary children, so it has one too). What must hold: a
    // node whose type allows no children really has none, so no node with children loses its button.
    [Fact]
    public void CanHaveChildren_IsFalseOnlyForNodesWithoutChildren() {
        Assert.True(ModelEditing.CanHaveChildren(OrderListView.Columns));
        var leaves = new List<IModelNode>();
        var queue = new Queue<IModelNode>([OrderListView]);
        while (queue.Count > 0) {
            var node = queue.Dequeue();
            if (!ModelEditing.CanHaveChildren(node)) leaves.Add(node);
            foreach (var child in ModelEditing.Children(node)) queue.Enqueue(child);
        }
        Assert.NotEmpty(leaves);
        Assert.All(leaves, leaf => Assert.Empty(ModelEditing.Children(leaf)));
    }

    // MODELEDITOR-013: the icon the Visual Studio Model Editor shows, the first [ImageName] on the node's interfaces
    // (ModelInterfaceAdapter.GetImageInfo, ModelInterfaceAdapter.cs 115-127).
    [Fact]
    public void ImageName_IsTheImageNameOnTheNodesInterface() {
        Assert.Equal("ModelEditor_Views", ModelEditing.ImageName(fixture.Model.Views));
        Assert.Equal("ModelEditor_ListView", ModelEditing.ImageName(OrderListView));
    }

    [Fact]
    public void ImageName_FallsBackToTheDefaultImage_WithoutAnImageNameAttribute() =>
        Assert.Equal("ModelEditor_Default", ModelEditing.ImageName(OrderListView.Columns));

    [Fact]
    public void IsModified_MarksANodeWithDifferencesInTheWritableLayer() {
        var view = fixture.Class<ModelTestContact>().DefaultListView;
        try {
            Assert.False(ModelEditing.IsModified(view));
            ModelEditing.SetText(view, "Caption", "Changed");
            Assert.True(ModelEditing.IsModified(view));
        }
        finally {
            ModelEditing.Reset(view, "Caption");
        }
    }

    [Fact]
    public void Values_CarryCategoryDescriptionAndLocalizableCue() {
        var rows = ModelEditing.Values(OrderListView);
        var allowEdit = Assert.Single(rows, r => r.Name == "AllowEdit");
        Assert.Equal("Behavior", allowEdit.Category);
        Assert.Contains("Property type", allowEdit.Description);
        Assert.True(Assert.Single(rows, r => r.Name == "Caption").IsLocalizable);
        Assert.False(allowEdit.IsLocalizable);
    }

    // CalculatePropertyVisible (ModelAttributesPropertyGridHelper.cs 507-510): Index is not offered for a node right under
    // the application root.
    [Fact]
    public void Values_HideIndex_ForANodeUnderTheRoot() {
        var views = (IModelNode)fixture.Model.Views;
        Assert.DoesNotContain(ModelEditing.Values(views), r => r.Name == "Index");
        Assert.Contains(ModelEditing.Values(OrderListView.Columns["Number"]!), r => r.Name == "Index");
    }

    // Codex review: the descriptions carry <b> and <br> but also generic type names such as System.Nullable<System.Int32>
    // (ModelEditorHelper.GetFriendlyTypeName); rendered raw, the type argument would be taken for an HTML element.
    [Fact]
    public void DescriptionHtml_KeepsTheFormattingTags_AndEncodesEverythingElse() {
        Assert.Equal("<b>Property type: </b>System.Nullable&lt;System.Int32&gt;<br>a &amp; b",
            ModelEditing.DescriptionHtml("<b>Property type: </b>System.Nullable<System.Int32><br>a & b"));
        var index = Assert.Single(ModelEditing.Values(OrderListView.Columns["Number"]!), r => r.Name == "Index");
        Assert.Contains("&lt;System.Int32&gt;", ModelEditing.DescriptionHtml(index.Description));
    }

    [Fact]
    public void Search_FindsNodesByCaption_AndGivesTheirPath() {
        var found = ModelEditing.Search(fixture.Model, "modeltestorder_listview", limit: 10);
        Assert.Contains(found, n => ModelEditing.Path(n) == "Views/ModelTestOrder_ListView");
    }
}
