using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.DomainLogics;
using DevExpress.ExpressApp.Model.NodeGenerators;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafLayoutBuilder.Sample.Blazor.Server;

[ToolboxItemFilter("Xaf.Platform.Blazor")]
// For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.ModuleBase.
public sealed class SampleBlazorModule : ModuleBase {
    //void Application_CreateCustomModelDifferenceStore(object sender, CreateCustomModelDifferenceStoreEventArgs e) {
    //    e.Store = new ModelDifferenceDbStore((XafApplication)sender, typeof(ModelDifference), true, "Blazor");
    //    e.Handled = true;
    //}
    void Application_CreateCustomUserModelDifferenceStore(object sender, CreateCustomModelDifferenceStoreEventArgs e) {
        e.Store = new ModelDifferenceDbStore((XafApplication)sender, typeof(ModelDifference), false, "Blazor");
        e.Handled = true;
    }
    /// <summary>
    /// E2E fixture (FREEZE-001), set by the host's --freeze-order-columns switch: adds Fixtures/FrozenOrderColumns.xafml as
    /// an extra application-level model difference store, next to Model.xafml.
    /// </summary>
    public static bool FreezeOrderColumnsFixture { get; set; }

    public SampleBlazorModule() {
    }
    public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB) {
        return ModuleUpdater.EmptyModuleUpdaters;
    }
    public override void Setup(XafApplication application) {
        base.Setup(application);
        // Uncomment this code to store the shared model differences (administrator settings in Model.XAFML) in the database.
        // For more information, refer to the following topic: https://docs.devexpress.com/eXpressAppFramework/113698/
        //application.CreateCustomModelDifferenceStore += Application_CreateCustomModelDifferenceStore;
        application.CreateCustomUserModelDifferenceStore += Application_CreateCustomUserModelDifferenceStore;
        if (FreezeOrderColumnsFixture) {
            // Handled stays false, so XAF still creates the default Model.xafml store (XafApplication.CreateModelDifferenceStore).
            application.CreateCustomModelDifferenceStore += (_, e) => e.AddExtraDiffStore(
                "FrozenOrderColumns", new FileModelStore(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "FrozenOrderColumns"));
        }
    }
}
