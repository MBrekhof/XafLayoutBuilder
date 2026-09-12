using System.ComponentModel;
using System.Diagnostics;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;

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
    public const string EnabledKey = "XafLayoutBuilder.EnableExport";
    public const string AdminKey = "XafLayoutBuilder.IsAdmin";

    public SimpleAction ExportLayoutAction { get; }

    /// <summary>The same export as a <c>LayoutSpecs</c> JSON document, the form <c>LayoutRegistry.RegisterJson</c> reads.</summary>
    public SimpleAction ExportLayoutJsonAction { get; }

    public ExportLayoutController() {
        ExportLayoutAction = new SimpleAction(this, "ExportLayoutToCode", PredefinedCategory.Tools) {
            Caption = "Export Layout To Code",
            ImageName = "Action_Export",
            ToolTip = "Print this type's DetailView layout and ListView columns as XafLayoutBuilder C#",
        };
        ExportLayoutAction.Execute += (_, e) => Show(LayoutCodePrinter.ForView(Application, View));
        ExportLayoutJsonAction = new SimpleAction(this, "ExportLayoutToJson", PredefinedCategory.Tools) {
            Caption = "Export Layout To JSON",
            ImageName = "Action_Export",
            ToolTip = "Print this type's DetailView layout and ListView columns as a LayoutSpecs JSON document",
        };
        ExportLayoutJsonAction.Execute += (_, e) => Show(LayoutCodePrinter.JsonForView(Application, View));
    }

    protected override void OnActivated() {
        base.OnActivated();
        foreach (var action in new[] { ExportLayoutAction, ExportLayoutJsonAction }) {
            action.Active[EnabledKey] = IsEnabled;
            action.Active[AdminKey] = IsAdministrator();
        }
    }

    /// <summary>The same gate the Blazor add-on's clipboard action uses.</summary>
    public static bool IsEnabled => Debugger.IsAttached || XafLayoutBuilderModule.EnableExport;

    /// <summary>
    /// Administrators only. A host without a security system has no roles to ask, and every user of such an
    /// application is effectively the developer, so the action stays available there; the debugger or
    /// <see cref="XafLayoutBuilderModule.EnableExport"/> condition is what gates it in that case.
    /// </summary>
    public static bool IsAdministrator() {
        if (SecuritySystem.Instance is null) return true;
        return SecuritySystem.CurrentUser is ISecurityUserWithRoles u
            && u.Roles.Any(r => r is IPermissionPolicyRole { IsAdministrative: true });
    }

    void Show((string FileName, string Text) printed) {
        var (fileName, code) = printed;
        var os = Application.CreateObjectSpace(typeof(LayoutCode));
        var layoutCode = os.CreateObject<LayoutCode>();
        layoutCode.Code = code;
        var view = Application.CreateDetailView(os, layoutCode, true);
        view.Caption = fileName;
        view.ViewEditMode = DevExpress.ExpressApp.Editors.ViewEditMode.Edit;
        Application.ShowViewStrategy.ShowViewInPopupWindow(view);
    }
}
