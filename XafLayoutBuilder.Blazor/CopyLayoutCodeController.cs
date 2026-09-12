using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Blazor.Internal;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Blazor;

/// <summary>
/// Puts the exported code straight on the clipboard, so "design in the app, paste the code" is one click instead of
/// select-all inside the popup memo. It sits next to Export Layout To Code in the Tools tab of the view you are
/// looking at: XAF Blazor's popup for a non-persistent object renders only its own OK and Cancel buttons, so an
/// action added to that popup's view would never appear.
/// </summary>
public sealed class CopyLayoutCodeController : ViewController<ObjectView> {
    public SimpleAction CopyAction { get; }

    public CopyLayoutCodeController() {
        CopyAction = new SimpleAction(this, "CopyLayoutCode", PredefinedCategory.Tools) {
            Caption = "Copy Layout To Clipboard",
            ImageName = "Action_Copy",
            ToolTip = "Copy this view's layout, printed as XafLayoutBuilder C#, to the clipboard",
        };
        CopyAction.Execute += async (_, _) => await CopyAsync();
    }

    protected override void OnActivated() {
        base.OnActivated();
        // Same gate as the export itself: administrators, and only when the host enabled it.
        CopyAction.Active[ExportLayoutController.EnabledKey] = ExportLayoutController.IsEnabled;
        CopyAction.Active[ExportLayoutController.AdminKey] = ExportLayoutController.IsAdministrator();
    }

    async Task CopyAsync() {
        var code = LayoutCodePrinter.ForView(Application, View).Code;
        // IXafJSRuntime is XAF's own wrapper around IJSRuntime. It is marked EditorBrowsable(Never), which is the one
        // DevExpress-internal thing this project leans on; if it disappears, inject IJSRuntime directly instead.
        var js = ((BlazorApplication)Application).ServiceProvider.GetRequiredService<IXafJSRuntime>();
        // navigator.clipboard needs a secure context: https, or http on localhost, which is how the sample runs.
        await js.InvokeVoidAsync("navigator.clipboard.writeText", code);
    }
}
