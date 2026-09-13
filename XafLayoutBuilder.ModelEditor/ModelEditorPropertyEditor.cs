using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// Hosts <see cref="ModelEditorComponent"/> in the popup's DetailView: a component model whose properties match the
/// component's parameters (dxdocs 405922). <see cref="IComplexViewItem"/> hands it the application whose model it edits.
/// </summary>
[PropertyEditor(typeof(string), Alias, false)]
public sealed class ModelEditorPropertyEditor(Type objectType, IModelMemberViewItem model)
    : BlazorPropertyEditorBase(objectType, model), IComplexViewItem {
    public const string Alias = "XafLayoutBuilder.ModelEditor";

    XafApplication? application;

    void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application) => this.application = application;

    protected override IComponentModel CreateComponentModel() => new ModelEditorComponentModel { Application = application! };
}

public sealed class ModelEditorComponentModel : ComponentModelBase {
    public XafApplication Application {
        get => GetPropertyValue<XafApplication>();
        set => SetPropertyValue(value);
    }

    public override Type ComponentType => typeof(ModelEditorComponent);
}
