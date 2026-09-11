using System.ComponentModel;
using System.Diagnostics;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
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
/// "Export Layout To Code" on any DetailView or ListView: walks the merged model (every layer applied) of the
/// type's default views into specs and prints them as the fluent builder. Visible only for administrators, and only
/// with a debugger attached or <see cref="XafLayoutBuilderModule.EnableExport"/> set by the host.
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

    static bool IsAdministrator() {
        if (SecuritySystem.Instance is null) return true; // no security: every user is the developer
        return SecuritySystem.CurrentUser is ISecurityUserWithRoles u
            && u.Roles.Any(r => r is IPermissionPolicyRole { IsAdministrative: true });
    }

    void Export() {
        var type = View.ObjectTypeInfo.Type;
        var modelClass = Application.Model.BOModel.GetClass(type);
        var notes = new List<string>();
        DetailLayoutSpec? detail = null;
        if (modelClass.DefaultDetailView is { } dv) {
            var (spec, skipped) = LayoutExporter.ExportDetail(dv);
            detail = spec;
            notes.AddRange(skipped);
        }
        var columns = modelClass.DefaultListView is { } lv ? LayoutExporter.ExportColumns(lv, modelClass.DefaultLookupListView) : null;
        notes.Insert(0, $"Exported from the running model ({DateTime.Now:yyyy-MM-dd HH:mm}); every layer applied. Save as {type.Name}.Layout.cs.");

        var os = Application.CreateObjectSpace(typeof(LayoutCode));
        var code = os.CreateObject<LayoutCode>();
        code.Code = CSharpLayoutPrinter.PrintClass(type.Name, detail, columns, notes);
        var view = Application.CreateDetailView(os, code, true);
        view.Caption = $"{type.Name}.Layout.cs";
        view.ViewEditMode = DevExpress.ExpressApp.Editors.ViewEditMode.Edit;
        Application.ShowViewStrategy.ShowViewInPopupWindow(view);
    }
}
