using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;

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

    /// <summary>MODELEDITOR-010: the shared differences the editor edits instead of the user's, opened when the window says so.</summary>
    public SharedModelSession? Shared { get; private set; }

    /// <summary>Runs the action against the editor's model: inside the shared session's storage when there is one.</summary>
    public void InModel(Action action) {
        if (Shared is null) action();
        else Shared.Run(action);
    }

    void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application) => this.application = application;

    // MODELEDITOR-003 review: no "Model" caption beside the editor. XAF Blazor asks the view item when the layout item sets no
    // ShowCaption (LayoutComponent.razor.cs 206), so nothing is written to the user's model; DetailPropertyEditor does the same.
    public override bool IsCaptionVisible => false;

    protected override IComponentModel CreateComponentModel() {
        var window = CurrentObject as ModelEditorWindow;
        if (window is { Shared: true } && Shared is null) Shared = SharedModel.Open(application!);
        // MODELEDITOR-014: the session reads and resets values on the writable layer's own node, which needs the model.
        Session.Model = Shared?.Model ?? (ModelApplicationBase)application!.Model;
        return new ModelEditorComponentModel {
            Application = application!,
            Model = Session.Model,
            Session = Session,
            Shared = Shared,
            StartViewId = window?.StartViewId,
        };
    }
}

public sealed class ModelEditorComponentModel : ComponentModelBase {
    public XafApplication Application {
        get => GetPropertyValue<XafApplication>();
        set => SetPropertyValue(value);
    }

    public ModelApplicationBase Model {
        get => GetPropertyValue<ModelApplicationBase>();
        set => SetPropertyValue(value);
    }

    public ModelEditSession Session {
        get => GetPropertyValue<ModelEditSession>();
        set => SetPropertyValue(value);
    }

    public SharedModelSession? Shared {
        get => GetPropertyValue<SharedModelSession?>();
        set => SetPropertyValue(value);
    }

    public string? StartViewId {
        get => GetPropertyValue<string?>();
        set => SetPropertyValue(value);
    }

    public override Type ComponentType => typeof(ModelEditorComponent);
}
