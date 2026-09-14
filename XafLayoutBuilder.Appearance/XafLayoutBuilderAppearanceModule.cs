using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.Model.Core;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Appearance;

/// <summary>
/// APPEAR-001: appearance rules declared in the builder (<c>ISupportAppearanceRules</c>, <see cref="AppearanceRegistry"/>),
/// applied through XAF's Conditional Appearance module. Add it next to <see cref="XafLayoutBuilderModule"/>; it brings
/// that module along, so an application without rules never references it.
/// </summary>
public sealed class XafLayoutBuilderAppearanceModule : ModuleBase {
    public XafLayoutBuilderAppearanceModule() {
        RequiredModuleTypes.Add(typeof(XafLayoutBuilderModule));
        RequiredModuleTypes.Add(typeof(ConditionalAppearanceModule));
        // The Module's exports print what this add-on reads back from the model.
        LayoutCodePrinter.AppearanceExport = AppearanceExporter.Export;
    }

    public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters) {
        base.AddGeneratorUpdaters(updaters);
        updaters.Add(new AppearanceRulesUpdater());
    }

    public override void Setup(XafApplication application) {
        base.Setup(application);
        application.SetupComplete += (_, _) => AppearanceStartupCheck.Run(application);
    }
}
