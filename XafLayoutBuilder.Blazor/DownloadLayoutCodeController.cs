using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor.Internal;
using Microsoft.JSInterop;

namespace XafLayoutBuilder.Blazor;

/// <summary>
/// Hands the exported code to the browser as {Type}.Layout.cs, so the file lands in the downloads folder ready to
/// drop into the project, or the same export as {Type}.layout.json for <c>LayoutRegistry.RegisterJson</c>. Sits next to
/// Export Layout To Code and Copy Layout To Clipboard in the Tools tab.
/// </summary>
public sealed class DownloadLayoutCodeController : LayoutCodeActionController {
    const string ModulePath = "./_content/XafLayoutBuilder.Blazor/xaflayoutbuilder.js";

    public SimpleAction DownloadAction { get; }
    public SimpleAction DownloadJsonAction { get; }

    public DownloadLayoutCodeController() {
        DownloadAction = CreateAction("DownloadLayoutCode", "Download Layout File", "Action_Download",
            "Download this view's layout as a {Type}.Layout.cs file");
        DownloadJsonAction = CreateAction("DownloadLayoutJson", "Download Layout JSON", "Action_Download",
            "Download this view's layout as a {Type}.layout.json document for LayoutRegistry.RegisterJson", json: true);
    }

    protected override async Task ExecuteAsync(IXafJSRuntime js, string fileName, string code) {
        // A server-side action cannot start a download by itself, so the add-on ships one JS module that creates the
        // anchor and clicks it. This is the only JavaScript in the repository; everything else is C#.
        // Disposal can throw when the circuit dies mid-call, which is why the base class wraps the whole thing.
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        var type = fileName.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "text/plain;charset=utf-8";
        await module.InvokeVoidAsync("downloadText", fileName, code, type);
    }
}
