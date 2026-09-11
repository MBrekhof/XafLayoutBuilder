using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model.Core;

namespace XafLayoutBuilder.Module;

public sealed class XafLayoutBuilderModule : ModuleBase {
    /// <summary>Shows "Export Layout To Code" without a debugger. Set by the host from its configuration; never an environment variable.</summary>
    public static bool EnableExport { get; set; }

    public XafLayoutBuilderModule() {
        AdditionalExportedTypes.Add(typeof(LayoutCode));
    }

    public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters) {
        base.AddGeneratorUpdaters(updaters);
        updaters.Add(new DetailViewLayoutUpdater());
        updaters.Add(new ListViewColumnsUpdater());
    }

    public override void Setup(XafApplication application) {
        base.Setup(application);
        application.SetupComplete += (_, _) => LayoutStartupCheck.Run(application);
    }
}
