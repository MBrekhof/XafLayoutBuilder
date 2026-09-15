using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// MODELEDITOR-010, in the popup itself: closing with unsaved edits is refused once, with a warning; the next close discards
/// them. The popup's Cancel (DialogController.Cancel, DialogController.cs 146-150) and its close button
/// (PopupWindowTemplateClosingController.cs 69-84) both call Window.Close, and BlazorWindow.Close raises its cancellable
/// Closing before it closes the view without asking it (BlazorWindow.cs 66-88), so the view's QueryCanClose never fires.
/// </summary>
public sealed class ModelEditorWindowController : ViewController<DetailView> {
    public ModelEditorWindowController() => TargetObjectType = typeof(ModelEditorWindow);

    protected override void OnActivated() {
        base.OnActivated();
        if (Frame is BlazorWindow window) window.Closing += Closing;
    }

    protected override void OnDeactivated() {
        if (Frame is BlazorWindow window) window.Closing -= Closing;
        base.OnDeactivated();
    }

    void Closing(object? sender, CancelEventArgs e) {
        if (View is not { IsDisposed: false } view) return;
        foreach (var editor in view.GetItems<ModelEditorPropertyEditor>()) {
            if (!editor.Session.HasPendingEdits || editor.Session.CloseWarned) continue;
            editor.Session.CloseWarned = true;
            e.Cancel = true;
            Application.ShowViewStrategy.ShowMessage(new MessageOptions {
                Message = "Unsaved edits: press Save to keep them, or close again to discard them.",
                Type = InformationType.Warning,
                Duration = 5000,
            });
        }
    }
}

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

    /// <summary>MODELEDITOR-010: the editor over the shared (administrator) differences, for users who may write them.</summary>
    public SimpleAction EditSharedModelAction { get; }

    public ModelEditorController() {
        EditModelAction = new SimpleAction(this, "XafLayoutBuilder.EditModel", PredefinedCategory.Tools) {
            Caption = "Edit Model",
            ImageName = "Action_EditModel",
            ToolTip = "Edit the application model; changes are saved to your own model differences",
        };
        EditModelAction.Execute += (_, _) => Show(null, shared: false);
        ViewInModelAction = new SimpleAction(this, "XafLayoutBuilder.ViewInModel", PredefinedCategory.Tools) {
            Caption = "View in Model",
            ImageName = "Action_EditModel",
            ToolTip = "Open the Model Editor on this view's node",
        };
        ViewInModelAction.Execute += (_, _) => Show(View.Id, shared: false);
        EditSharedModelAction = new SimpleAction(this, "XafLayoutBuilder.EditSharedModel", PredefinedCategory.Tools) {
            Caption = "Edit Shared Model",
            ImageName = "Action_EditModel",
            ToolTip = "Edit the shared model differences every user starts from; changes show to everyone at their next page load",
        };
        EditSharedModelAction.Execute += (_, _) => Show(null, shared: true);
    }

    protected override void OnActivated() {
        base.OnActivated();
        var allowed = CanEditModel(Application);
        EditModelAction.Active["Security"] = allowed;
        ViewInModelAction.Active["Security"] = allowed;
        // ponytail: one query per view activation (the shared record's permission); cache per user if it ever shows.
        EditSharedModelAction.Active["Security"] = allowed && SharedModel.CanEdit(Application);
    }

    public static bool CanEditModel(XafApplication application) =>
        application.Security is not IRequestSecurity security || security.IsGranted(new ModelOperationPermissionRequest());

    void Show(string? startViewId, bool shared) {
        var os = Application.CreateObjectSpace(typeof(ModelEditorWindow));
        var window = os.CreateObject<ModelEditorWindow>();
        window.StartViewId = startViewId;
        window.Shared = shared;
        var view = Application.CreateDetailView(os, window, true);
        view.Caption = shared ? "Model Editor (shared model)" : "Model Editor";
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
        EventHandler<CreateCustomModelDifferenceStoreEventArgs> beforeSave = (_, e) => BeforeSave(view, e);
        application.CreateCustomUserModelDifferenceStore += beforeSave;
        view.Closed += (_, _) => {
            application.CreateCustomUserModelDifferenceStore -= beforeSave;
            if (view.IsDisposed) return;
            foreach (var editor in view.GetItems<ModelEditorPropertyEditor>()) {
                editor.InModel(editor.Session.RollbackAdded);
                editor.Shared?.Dispose();
            }
        };
        application.ShowViewStrategy.ShowViewInPopupWindow(view);
    }

    // ponytail: a view disposed without Closed leaves the handler subscribed until the application goes; it does nothing then.
    static void BeforeSave(DetailView view, CreateCustomModelDifferenceStoreEventArgs e) {
        if (view.IsDisposed) return;
        foreach (var editor in view.GetItems<ModelEditorPropertyEditor>()) {
            if (editor.Session.Saving) continue;
            // A shared session's nodes and edits live in its own model, which no user-model save touches.
            if (editor.Shared is not null) continue;
            // Edits an Apply wrote to the user layer and Reload then discarded cannot be taken out of the live model; this
            // circuit's user model must not be stored at all then (SaveModelChanges saves nothing without a store,
            // XafApplication.cs 2497-2506), the reload's deferred flush included. The next circuit reads what is stored (Codex
            // review 2). ponytail: the circuit's other runtime customisations since its last save go with them.
            if (editor.Session.DiscardedApplied) {
                e.Store = null;
                e.Handled = true;
                continue;
            }
            editor.Session.RollbackAdded();
            editor.Session.ReplaySaved();
        }
    }
}
