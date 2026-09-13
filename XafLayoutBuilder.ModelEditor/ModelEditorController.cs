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

    /// <summary>
    /// Opens the editor on the current view's node, as the WinForms View in Model action focuses it before EditModel
    /// (ViewInModelController.cs 125-137). ponytail: the view node only, not WinForms' class and validation rule items.
    /// </summary>
    public SimpleAction ViewInModelAction { get; }

    public ModelEditorController() {
        EditModelAction = new SimpleAction(this, "XafLayoutBuilder.EditModel", PredefinedCategory.Tools) {
            Caption = "Edit Model",
            ImageName = "Action_EditModel",
            ToolTip = "Edit the application model; changes are saved to your own model differences",
        };
        EditModelAction.Execute += (_, _) => Show(null);
        ViewInModelAction = new SimpleAction(this, "XafLayoutBuilder.ViewInModel", PredefinedCategory.Tools) {
            Caption = "View in Model",
            ImageName = "Action_EditModel",
            ToolTip = "Open the Model Editor on this view's node",
        };
        ViewInModelAction.Execute += (_, _) => Show(View.Id);
    }

    protected override void OnActivated() {
        base.OnActivated();
        var allowed = CanEditModel(Application);
        EditModelAction.Active["Security"] = allowed;
        ViewInModelAction.Active["Security"] = allowed;
    }

    public static bool CanEditModel(XafApplication application) =>
        application.Security is not IRequestSecurity security || security.IsGranted(new ModelOperationPermissionRequest());

    void Show(string? startViewId) {
        var os = Application.CreateObjectSpace(typeof(ModelEditorWindow));
        var window = os.CreateObject<ModelEditorWindow>();
        window.StartViewId = startViewId;
        var view = Application.CreateDetailView(os, window, true);
        view.Caption = "Model Editor";
        view.ViewEditMode = ViewEditMode.Edit;
        // XAF also saves the user model without the editor: the deferred save it flushes when the circuit closes or the same
        // user logs on again (docs/api-notes.md, "Model saves the editor does not start"). Nodes added in the editor exist in
        // the live model at once, so such a save would store them; and it first lets the open views write their state into
        // the model, so after Save and the reload the old circuit's views would write theirs over the saved edits (a grid
        // hides a column it does not show). SaveModelChanges asks for the store right before it saves (XafApplication.cs
        // 2497-2506): there, unless the save is the editor's own, the nodes added but not saved are removed and the saved
        // edits are written again. Closing the popup without Save removes the added nodes too (View.Close raises Closed
        // synchronously, View.cs 286-302).
        var application = Application;
        EventHandler<CreateCustomModelDifferenceStoreEventArgs> beforeSave = (_, _) => BeforeSave(view);
        application.CreateCustomUserModelDifferenceStore += beforeSave;
        view.Closed += (_, _) => {
            application.CreateCustomUserModelDifferenceStore -= beforeSave;
            if (view.IsDisposed) return;
            foreach (var editor in view.GetItems<ModelEditorPropertyEditor>()) editor.Session.RollbackAdded();
        };
        application.ShowViewStrategy.ShowViewInPopupWindow(view);
    }

    // ponytail: a view disposed without Closed leaves the handler subscribed until the application goes; it does nothing then.
    static void BeforeSave(DetailView view) {
        if (view.IsDisposed) return;
        foreach (var editor in view.GetItems<ModelEditorPropertyEditor>()) {
            if (editor.Session.Saving) continue;
            editor.Session.RollbackAdded();
            editor.Session.ReplaySaved();
        }
    }
}
