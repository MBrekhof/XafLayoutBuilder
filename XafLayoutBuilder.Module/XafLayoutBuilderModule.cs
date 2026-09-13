using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model.Core;

namespace XafLayoutBuilder.Module;

public sealed class XafLayoutBuilderModule : ModuleBase {
    /// <summary>Shows "Export Layout To Code" without a debugger. Set by the host from its configuration; never an environment variable.</summary>
    public static bool EnableExport { get; set; }

    /// <summary>
    /// What a layout that cannot be applied does. Off (the default): it is logged through XAF's Tracing
    /// (eXpressAppFramework.log) and the view keeps XAF's own layout or columns, so one broken layout never stops the
    /// application. On: the startup check throws, naming every broken view, and the application stops at startup.
    /// Turn it on in development and CI. Set by the host from its configuration; never an environment variable.
    /// </summary>
    public static bool FailFastOnLayoutErrors { get; set; }

    public XafLayoutBuilderModule() {
        AdditionalExportedTypes.Add(typeof(LayoutCode));
    }

    public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters) {
        base.AddGeneratorUpdaters(updaters);
        updaters.Add(new DeclaredViewsUpdater());
        updaters.Add(new DetailViewLayoutUpdater());
        updaters.Add(new ListViewColumnsUpdater());
    }

    public override void Setup(XafApplication application) {
        base.Setup(application);
        application.SetupComplete += (_, _) => LayoutStartupCheck.Run(application);
    }
}
