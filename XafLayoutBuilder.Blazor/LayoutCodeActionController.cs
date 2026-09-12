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
/// Shared plumbing for the two browser-side actions. XAF runs an action's Execute handler synchronously and considers
/// the action finished when it returns, so an `async` handler would leave its exceptions unobserved: a denied
/// clipboard, a failed module import or a circuit that went away would escape XAF's own error handling. Everything
/// here therefore runs inside one task that never throws, with a busy flag so a second click cannot start a second
/// run over the top of the first.
/// </summary>
public abstract class LayoutCodeActionController : ViewController<ObjectView> {
    int running;

    protected SimpleAction CreateAction(string id, string caption, string imageName, string toolTip) {
        var action = new SimpleAction(this, id, PredefinedCategory.Tools) {
            Caption = caption,
            ImageName = imageName,
            ToolTip = toolTip,
        };
        action.Execute += (_, _) => _ = RunAsync(action);
        return action;
    }

    protected override void OnActivated() {
        base.OnActivated();
        // Same gate as the export itself: administrators, and only when the host enabled it.
        foreach (var action in Actions.OfType<SimpleAction>()) {
            action.Active[ExportLayoutController.EnabledKey] = ExportLayoutController.IsEnabled;
            action.Active[ExportLayoutController.AdminKey] = ExportLayoutController.IsAdministrator();
        }
    }

    /// <summary>Does the work. Called with the printed code for the current view and XAF's JS runtime.</summary>
    protected abstract Task ExecuteAsync(IXafJSRuntime js, string fileName, string code);

    async Task RunAsync(ActionBase action) {
        if (Interlocked.Exchange(ref running, 1) == 1) return; // still busy with the previous click
        try {
            var (fileName, code) = LayoutCodePrinter.ForView(Application, View);
            var js = ((BlazorApplication)Application).ServiceProvider.GetRequiredService<IXafJSRuntime>();
            await ExecuteAsync(js, fileName, code);
        }
        catch (JSDisconnectedException) {
            // The circuit is gone; there is no browser left to tell, and nothing was half-written.
        }
        catch (OperationCanceledException) {
        }
        catch (Exception ex) {
            TryReport($"{action.Caption} failed: {ex.Message}");
        }
        finally {
            Interlocked.Exchange(ref running, 0);
        }
    }

    void TryReport(string message) {
        // Reporting needs the circuit too, so it gets the same treatment.
        try { Application.ShowViewStrategy.ShowMessage(message, InformationType.Error, 10000); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }
}
