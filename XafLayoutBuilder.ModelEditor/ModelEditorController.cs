using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// "Edit Model" in the Tools tab of any object view, opening <see cref="ModelEditorWindow"/> in a popup. The gate is the
/// WinForms Edit Model action's (EditModelController.cs 67-71): the user's role must be allowed to edit the model
/// (CanEditModel; IsAdministrative alone is not enough), and an application without a request security system allows it.
/// </summary>
public sealed class ModelEditorController : ViewController<ObjectView> {
    public SimpleAction EditModelAction { get; }

    public ModelEditorController() {
        EditModelAction = new SimpleAction(this, "XafLayoutBuilder.EditModel", PredefinedCategory.Tools) {
            Caption = "Edit Model",
            ImageName = "Action_EditModel",
            ToolTip = "Edit the application model; changes are saved to your own model differences",
        };
        EditModelAction.Execute += (_, _) => Show();
    }

    protected override void OnActivated() {
        base.OnActivated();
        EditModelAction.Active["Security"] = CanEditModel(Application);
    }

    public static bool CanEditModel(XafApplication application) =>
        application.Security is not IRequestSecurity security || security.IsGranted(new ModelOperationPermissionRequest());

    void Show() {
        var os = Application.CreateObjectSpace(typeof(ModelEditorWindow));
        var view = Application.CreateDetailView(os, os.CreateObject<ModelEditorWindow>(), true);
        view.Caption = "Model Editor";
        view.ViewEditMode = ViewEditMode.Edit;
        Application.ShowViewStrategy.ShowViewInPopupWindow(view);
    }
}
