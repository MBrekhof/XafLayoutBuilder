using System.ComponentModel;
using System.Diagnostics;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.NodeGenerators;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;
using XafLayoutBuilder.Core;

namespace XafLayoutBuilder.Module;

/// <summary>What the popup shows: the printed class for the current view's type. Nothing is written to disk.</summary>
[DomainComponent]
public class LayoutCode : NonPersistentBaseObject {
    // Editable on purpose: a read-only memo renders greyed out; nothing is persisted anyway.
    [FieldSize(FieldSizeAttribute.Unlimited)]
    [ModelDefault("RowCount", "30")]
    public virtual string Code { get; set; } = "";
}

/// <summary>
/// "Export Layout To Code" on any DetailView or ListView: walks the merged model (every layer applied) into specs and
/// prints them as the fluent builder. The view the action runs on is the one exported; the other half comes from the
/// type's default views, and the printed comment names the view ids so the result is never ambiguous.
/// Visible only for administrators, and only with a debugger attached or
/// <see cref="XafLayoutBuilderModule.EnableExport"/> set by the host.
/// </summary>
public sealed class ExportLayoutController : ViewController<ObjectView> {
    public SimpleAction ExportLayoutAction { get; }

    public ExportLayoutController() {
        ExportLayoutAction = new SimpleAction(this, "ExportLayoutToCode", PredefinedCategory.Tools) {
            Caption = "Export Layout To Code",
            ImageName = "Action_Export",
            ToolTip = "Print this type's DetailView layout and ListView columns as XafLayoutBuilder C#",
        };
        ExportLayoutAction.Execute += (_, e) => Export();
    }

    protected override void OnActivated() {
        base.OnActivated();
        var enabled = Debugger.IsAttached || XafLayoutBuilderModule.EnableExport;
        ExportLayoutAction.Active["XafLayoutBuilder.EnableExport"] = enabled;
        ExportLayoutAction.Active["XafLayoutBuilder.IsAdmin"] = IsAdministrator();
    }

    /// <summary>
    /// Administrators only. A host without a security system has no roles to ask, and every user of such an
    /// application is effectively the developer, so the action stays available there; the debugger or
    /// <see cref="XafLayoutBuilderModule.EnableExport"/> condition is what gates it in that case.
    /// </summary>
    static bool IsAdministrator() {
        if (SecuritySystem.Instance is null) return true;
        return SecuritySystem.CurrentUser is ISecurityUserWithRoles u
            && u.Roles.Any(r => r is IPermissionPolicyRole { IsAdministrative: true });
    }

    void Export() {
        var type = View.ObjectTypeInfo.Type;
        var modelClass = Application.Model.BOModel.GetClass(type);
        var notes = new List<string>();

        // Start document section 6: export what the user is looking at, not just the default views.
        var detailView = View is DetailView dv ? dv.Model : modelClass.DefaultDetailView;
        var listView = modelClass.DefaultListView;
        var lookupView = modelClass.DefaultLookupListView;
        if (View is ListView lv) {
            if (lv.Model.GetValue<bool>(ModelViewsNodesGenerator.IsLookupListView)) lookupView = lv.Model;
            else listView = lv.Model;
        }

        DetailLayoutSpec? detail = null;
        if (detailView is not null) {
            var (spec, skipped) = LayoutExporter.ExportDetail(detailView);
            detail = spec;
            notes.AddRange(skipped);
        }
        ListColumnsSpec? columns = null;
        if (listView is not null) {
            var (spec, skipped) = LayoutExporter.ExportColumns(listView, lookupView);
            columns = spec;
            notes.AddRange(skipped);
        }

        notes.Insert(0, $"Exported from the running model ({DateTime.Now:yyyy-MM-dd HH:mm}); every layer applied. Save as {type.Name}.Layout.cs.");
        notes.Insert(1, $"Views: {detailView?.Id ?? "(no DetailView)"}, {listView?.Id ?? "(no ListView)"}, {lookupView?.Id ?? "(no lookup)"}.");

        var os = Application.CreateObjectSpace(typeof(LayoutCode));
        var code = os.CreateObject<LayoutCode>();
        code.Code = CSharpLayoutPrinter.PrintClass(type.Namespace, type.Name, detail, columns, notes);
        var view = Application.CreateDetailView(os, code, true);
        view.Caption = $"{type.Name}.Layout.cs";
        view.ViewEditMode = DevExpress.ExpressApp.Editors.ViewEditMode.Edit;
        Application.ShowViewStrategy.ShowViewInPopupWindow(view);
    }
}
