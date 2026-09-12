using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Blazor.Internal;

namespace XafLayoutBuilder.Blazor;

/// <summary>
/// Puts the exported code straight on the clipboard, so "design in the app, paste the code" is one click instead of
/// select-all inside the popup memo. It sits next to Export Layout To Code in the Tools tab: XAF Blazor's popup for a
/// non-persistent object renders only its own OK and Cancel buttons, so an action added to that popup's view would
/// never appear.
/// </summary>
public sealed class CopyLayoutCodeController : LayoutCodeActionController {
    public SimpleAction CopyAction { get; }

    public CopyLayoutCodeController() =>
        CopyAction = CreateAction("CopyLayoutCode", "Copy Layout To Clipboard", "Action_Copy",
            "Copy this view's layout, printed as XafLayoutBuilder C#, to the clipboard");

    protected override async Task ExecuteAsync(IXafJSRuntime js, string fileName, string code) =>
        // navigator.clipboard needs a secure context: https, or http on localhost, which is how the sample runs.
        // IXafJSRuntime is XAF's own wrapper around IJSRuntime; it is marked EditorBrowsable(Never), which is the one
        // DevExpress internal this project leans on besides the generated column index.
        await js.InvokeVoidAsync("navigator.clipboard.writeText", code);
}
