using DevExpress.ExpressApp;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Blazor;

/// <summary>
/// The Blazor half of XafLayoutBuilder: everything that needs a browser. Today that is one button, the Copy action
/// on the export popup. Add it next to <see cref="XafLayoutBuilderModule"/> in a Blazor host; everything else works
/// without it.
/// </summary>
public sealed class XafLayoutBuilderBlazorModule : ModuleBase {
    public XafLayoutBuilderBlazorModule() {
        RequiredModuleTypes.Add(typeof(XafLayoutBuilderModule));
    }
}
