using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-007: the WinForms Model Editor's ModelValidator (DevExpress.ExpressApp.Win/Core/ModelEditor/ModelValidator.cs
// 47-93) requires the required values and the key property of a modified or new node. The model interfaces carry no rule
// attributes, so those are all its rules. The editor keeps edits pending until Save, so it checks the values the pending
// edits would write.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorValidationTests(ApplicationModelFixture fixture) {
    IModelListView OrderListView => fixture.Class<ModelTestOrder>().DefaultListView;

    // A column's PropertyName is required ([Required], IModelMemberViewItem); a pending empty text for it is a missing value.
    [Fact]
    public void Session_MissingRequired_CountsAPendingEmptyValue() {
        var column = OrderListView.Columns["Number"]!;
        var session = new ModelEditSession();
        Assert.DoesNotContain("PropertyName", session.MissingRequired(column));

        session.SetText(column, "PropertyName", "");
        Assert.Contains("PropertyName", session.MissingRequired(column));
    }

    // The card's test: a required value cleared blocks Save, naming the node and the value, and nothing is written.
    [Fact]
    public void Session_Apply_RefusesAClearedRequiredValue_AndWritesNothing() {
        var column = OrderListView.Columns["Number"]!;
        var session = new ModelEditSession();
        session.SetText(column, "PropertyName", "");
        session.SetText(OrderListView, "Caption", "Not written");

        var ex = Assert.Throws<InvalidOperationException>(() => session.Apply());
        Assert.Contains(ModelEditing.Path(column), ex.Message);
        Assert.Contains("PropertyName", ex.Message);
        Assert.Equal("Number", column.PropertyName);
        Assert.NotEqual("Not written", OrderListView.Caption);
    }
}
