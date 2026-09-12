using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Blazor.Internal;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Blazor;

/// <summary>
/// Hands the exported code to the browser as {Type}.Layout.cs, so the file lands in the downloads folder ready to
/// drop into the project. Sits next to Export Layout To Code and Copy Layout To Clipboard in the Tools tab.
/// </summary>
public sealed class DownloadLayoutCodeController : ViewController<ObjectView> {
    const string ModulePath = "./_content/XafLayoutBuilder.Blazor/xaflayoutbuilder.js";

    public SimpleAction DownloadAction { get; }

    public DownloadLayoutCodeController() {
        DownloadAction = new SimpleAction(this, "DownloadLayoutCode", PredefinedCategory.Tools) {
            Caption = "Download Layout File",
            ImageName = "Action_Download",
            ToolTip = "Download this view's layout as a {Type}.Layout.cs file",
        };
        DownloadAction.Execute += async (_, _) => await DownloadAsync();
    }

    protected override void OnActivated() {
        base.OnActivated();
        // Same gate as the export itself: administrators, and only when the host enabled it.
        DownloadAction.Active[ExportLayoutController.EnabledKey] = ExportLayoutController.IsEnabled;
        DownloadAction.Active[ExportLayoutController.AdminKey] = ExportLayoutController.IsAdministrator();
    }

    async Task DownloadAsync() {
        var (fileName, code) = LayoutCodePrinter.ForView(Application, View);
        var js = ((BlazorApplication)Application).ServiceProvider.GetRequiredService<IXafJSRuntime>();
        // A server-side action cannot start a download by itself, so the add-on ships one JS module that creates the
        // anchor and clicks it. This is the only JavaScript in the repository; everything else is C#.
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await module.InvokeVoidAsync("downloadText", fileName, code);
    }
}
