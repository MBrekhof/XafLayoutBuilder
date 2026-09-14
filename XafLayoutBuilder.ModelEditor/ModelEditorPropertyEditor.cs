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

    /// <summary>
    /// The popup's edits. Owned here rather than by the component so ModelEditorController can take back the nodes it added
    /// when the popup closes without Save (MODELEDITOR-004); the view's Closed event runs on Cancel, OK and the close button.
    /// </summary>
    public ModelEditSession Session { get; } = new();

    void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application) => this.application = application;

    // MODELEDITOR-003 review: no "Model" caption beside the editor. XAF Blazor asks the view item when the layout item sets no
    // ShowCaption (LayoutComponent.razor.cs 206), so nothing is written to the user's model; DetailPropertyEditor does the same.
    public override bool IsCaptionVisible => false;

    protected override IComponentModel CreateComponentModel() => new ModelEditorComponentModel {
        Application = application!,
        Session = Session,
        StartViewId = (CurrentObject as ModelEditorWindow)?.StartViewId,
    };
}

public sealed class ModelEditorComponentModel : ComponentModelBase {
    public XafApplication Application {
        get => GetPropertyValue<XafApplication>();
        set => SetPropertyValue(value);
    }

    public ModelEditSession Session {
        get => GetPropertyValue<ModelEditSession>();
        set => SetPropertyValue(value);
    }

    public string? StartViewId {
        get => GetPropertyValue<string?>();
        set => SetPropertyValue(value);
    }

    public override Type ComponentType => typeof(ModelEditorComponent);
}
