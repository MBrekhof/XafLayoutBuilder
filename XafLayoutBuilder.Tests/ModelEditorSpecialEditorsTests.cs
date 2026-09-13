using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model;
using XafLayoutBuilder.ModelEditor;

namespace XafLayoutBuilder.Tests;

// MODELEDITOR-006: the special editors the WinForms Model Editor attaches through [Editor] (docs/model-editor-scope.md,
// "Special editors"), recognised by their type names so the Blazor editor needs no WinForms reference.
[Collection(ApplicationModelCollection.Name)]
public class ModelEditorSpecialEditorsTests(ApplicationModelFixture fixture) {
    IModelListView OrderListView => fixture.Class<ModelTestOrder>().DefaultListView;

    static ModelValueRow Row(IModelNode node, string name) => Assert.Single(ModelEditing.Values(node), r => r.Name == name);

    // CriteriaModelEditorControl on IModelListView.Criteria and Filter (IModelListView.cs 102, 107), ExpressionModelEditorControl
    // on IModelMember.Expression (CommonInterfaces.cs 295), the multiline string editor on IModelToolTip.ToolTip (562),
    // ImageGalleryModelEditorControl on IModelView.ImageName (IModelView.cs 59).
    [Fact]
    public void SpecialEditor_FollowsTheEditorAttributeOfTheValue() {
        var member = fixture.Class<ModelTestOrder>().AllMembers.Single(m => m.Name == nameof(ModelTestOrder.Number));
        Assert.Equal(ModelValueEditor.Criteria, ModelEditing.SpecialEditor(OrderListView, "Criteria"));
        Assert.Equal(ModelValueEditor.Criteria, ModelEditing.SpecialEditor(OrderListView, "Filter"));
        Assert.Equal(ModelValueEditor.Expression, ModelEditing.SpecialEditor(member, "Expression"));
        Assert.Equal(ModelValueEditor.Multiline, ModelEditing.SpecialEditor(OrderListView.Columns["Number"]!, "ToolTip"));
        Assert.Equal(ModelValueEditor.Image, ModelEditing.SpecialEditor(OrderListView, "ImageName"));
        Assert.Equal(ModelValueEditor.Text, ModelEditing.SpecialEditor(OrderListView, "Caption"));
        Assert.Equal(ModelValueEditor.Criteria, Row(OrderListView, "Criteria").Editor);
    }

    // [CriteriaOptions("ModelClass.TypeInfo")] (IModelListView.cs 101), resolved as CriteriaModelEditorControl does (91-137).
    [Fact]
    public void CriteriaTypeInfo_OfAListViewsCriteria_IsItsClass() {
        Assert.Equal(typeof(ModelTestOrder), ModelEditing.CriteriaTypeInfo(OrderListView, "Criteria")!.Type);
        Assert.Null(ModelEditing.CriteriaTypeInfo(OrderListView, "Caption"));
    }

    // The fields XAF Blazor's criteria editor offers (DxFilterBuilderHelper.GetMembers, DxFilterBuilderAdapter.cs 205-259): a
    // reference's fields by full path, a collection's by their own name (DxFilterBuilderField docs).
    [Fact]
    public void FilterFields_OfAClass_FollowReferencesAndCollections() {
        var fields = ModelEditing.FilterFields(ModelEditing.CriteriaTypeInfo(OrderListView, "Criteria")!);

        Assert.Equal(typeof(string), Assert.Single(fields, f => f.FieldName == nameof(ModelTestOrder.Number)).Type);
        var customer = Assert.Single(fields, f => f.FieldName == nameof(ModelTestOrder.Customer));
        Assert.False(customer.IsCollection);
        Assert.Contains(customer.Fields, f => f.FieldName == "Customer.Name");
        var lines = Assert.Single(fields, f => f.FieldName == nameof(ModelTestOrder.Lines));
        Assert.True(lines.IsCollection);
        Assert.Contains(lines.Fields, f => f.FieldName == nameof(ModelTestLine.Product));
    }

    // Codex review 2: the running application's EF Core Customer is persistent, not a domain component, and XAF still nests its
    // fields: every non-list member of a non-simple type has children (DxFilterBuilderHelper.GetFieldModel,
    // DxFilterBuilderAdapter.cs 301-308, 335-337).
    [Fact]
    public void FilterFields_FollowAReferenceThatIsNoDomainComponent() {
        var owner = XafTypesInfo.Instance.FindTypeInfo(typeof(ModelTestPlainOwner));
        Assert.False(owner.FindMember(nameof(ModelTestPlainOwner.Part))!.MemberTypeInfo.IsDomainComponent);

        var part = Assert.Single(ModelEditing.FilterFields(owner), f => f.FieldName == nameof(ModelTestPlainOwner.Part));
        Assert.Contains(part.Fields, f => f.FieldName == "Part.Code");
    }

    // The card's round trip: criteria text written through the session is the ListView's Criteria after Apply.
    [Fact]
    public void Criteria_WrittenThroughTheSession_RoundTrips() {
        var criteria = CriteriaOperator.Parse("[Number] = 'ORD-001' And [Customer.Name] Like 'A%'").ToString();
        try {
            var session = new ModelEditSession();
            session.SetText(OrderListView, "Criteria", criteria);
            session.Apply();
            Assert.Equal(criteria, CriteriaOperator.Parse(OrderListView.Criteria).ToString());
        }
        finally {
            ModelEditing.Reset(OrderListView, "Criteria");
        }
    }
}
