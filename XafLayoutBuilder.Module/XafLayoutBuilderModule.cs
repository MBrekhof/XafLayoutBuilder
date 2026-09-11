using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model.Core;

namespace XafLayoutBuilder.Module;

public sealed class XafLayoutBuilderModule : ModuleBase {
    public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters) {
        base.AddGeneratorUpdaters(updaters);
        updaters.Add(new DetailViewLayoutUpdater());
        updaters.Add(new ListViewColumnsUpdater());
    }
}
