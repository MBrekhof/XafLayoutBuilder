using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ConditionalAppearance;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.Utils;
using XafLayoutBuilder.Module;

namespace XafLayoutBuilder.Tests;

/// <summary>
/// MODEL-001: a real Application Model for the updater and exporter tests, built in-process the way the Model Editor
/// builds one (DesignerModelFactory, docs/api-notes.md), with no host and no database. One model for the whole
/// collection: XafTypesInfo.Instance is process-wide, and a view's layout is generated once, on first read.
/// </summary>
public sealed class ApplicationModelFixture : IDisposable {
    readonly bool failFast = XafLayoutBuilderModule.FailFastOnLayoutErrors;

    public IModelApplication Model { get; }

    /// <summary>What reading ModelTestStrictBroken's layout threw, with FailFastOnLayoutErrors on.</summary>
    public Exception? StrictBrokenLayoutError { get; }

    /// <summary>Every node id of ModelTestDegradedBroken's layout, read with FailFastOnLayoutErrors off.</summary>
    public IReadOnlyList<string> DegradedBrokenLayoutIds { get; }

    /// <summary>APPEAR-001: what reading ModelTestAppearanceClashStrict's rules threw, with FailFastOnLayoutErrors on.</summary>
    public Exception? StrictAppearanceClashError { get; }

    /// <summary>APPEAR-001: ModelTestAppearanceClashDegraded's rule ids, read with FailFastOnLayoutErrors off.</summary>
    public IReadOnlyList<string> DegradedAppearanceClashRuleIds { get; }

    /// <summary>APPEAR-001, Codex review: ModelTestAppearanceBadCriteriaDegraded's rule ids, read with FailFastOnLayoutErrors off.</summary>
    public IReadOnlyList<string> DegradedBadCriteriaRuleIds { get; }

    /// <summary>APPEAR-001: what the first appearance startup check threw, the first reader of ModelTestAppearanceOnBrokenLayout's layout.</summary>
    public Exception? FirstAppearanceCheckError { get; }

    public ApplicationModelFixture() {
        // A layout error throws where the model is read instead of going to the trace log.
        XafLayoutBuilderModule.FailFastOnLayoutErrors = true;
        // Declared views are added when XAF generates the Views node, so they have to be registered before the model exists.
        ModelTestDeclaredViews.Register();
        var factory = new DesignerModelFactory();
        var module = new ModelTestModule();
        Model = factory.CreateApplicationModel(module, factory.CreateModulesManager(module, AppContext.BaseDirectory), ModelStoreBase.Empty);

        // The broken views and rules are read here, before any test runs. A layout is generated once, and anything that walks
        // the model first would generate them under whatever the switch happens to be at that moment: xUnit's failure
        // message for an assertion over model nodes prints them property by property, which generated the strict view's
        // layout inside the formatter and swallowed its exception.
        StrictBrokenLayoutError = Record.Exception(() => Class<ModelTestStrictBroken>().DefaultDetailView.Layout.Count);
        StrictAppearanceClashError = Record.Exception(() => Rules<ModelTestAppearanceClashStrict>().Count);
        XafLayoutBuilderModule.FailFastOnLayoutErrors = false;
        try {
            DegradedBrokenLayoutIds = LayoutIds(Class<ModelTestDegradedBroken>().DefaultDetailView.Layout).ToList();
            DegradedAppearanceClashRuleIds = Rules<ModelTestAppearanceClashDegraded>().Select(r => ((DevExpress.ExpressApp.Model.Core.ModelNode)r).Id).ToList();
            DegradedBadCriteriaRuleIds = Rules<ModelTestAppearanceBadCriteriaDegraded>().Select(r => ((DevExpress.ExpressApp.Model.Core.ModelNode)r).Id).ToList();
        }
        finally {
            XafLayoutBuilderModule.FailFastOnLayoutErrors = true;
        }
        // APPEAR-001, gate: the appearance check is the first to read ModelTestAppearanceOnBrokenLayout's layout, as it was for
        // Order under --break-layout. After the degraded reads, so it cannot generate those under fail-fast on.
        FirstAppearanceCheckError = Record.Exception(() => XafLayoutBuilder.Appearance.AppearanceStartupCheck.Check(Model));
    }

    public IModelClass Class<T>() => Model.BOModel.GetClass(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not in the BOModel; test types must be top-level public [DomainComponent] classes.");

    /// <summary>APPEAR-001: the class's AppearanceRules node, which the Conditional Appearance module adds to every class.</summary>
    public IModelAppearanceRules Rules<T>() => ((IModelConditionalAppearance)Class<T>()).AppearanceRules;

    /// <summary>Every group, tab and item id in a layout, depth first.</summary>
    public static IEnumerable<string> LayoutIds(IEnumerable<IModelViewLayoutElement> elements) =>
        elements.SelectMany(e => e switch {
            IModelTabbedGroup t => LayoutIds(t).Prepend(t.Id),
            IModelLayoutGroup g => LayoutIds(g).Prepend(g.Id),
            _ => [e.Id],
        });

    public void Dispose() => XafLayoutBuilderModule.FailFastOnLayoutErrors = failFast;

    internal sealed class ModelTestModule : ModuleBase {
        public ModelTestModule() {
            RequiredModuleTypes.Add(typeof(SystemModule));
            RequiredModuleTypes.Add(typeof(XafLayoutBuilderModule));
            // APPEAR-001: listed explicitly; whether the modules manager follows the add-on's own RequiredModuleTypes is not relied on.
            RequiredModuleTypes.Add(typeof(ConditionalAppearanceModule));
            RequiredModuleTypes.Add(typeof(XafLayoutBuilder.Appearance.XafLayoutBuilderAppearanceModule));
            foreach (var type in new[] { typeof(ModelTestCustomer), typeof(ModelTestLine), typeof(ModelTestOrder), typeof(ModelTestContact),
                         typeof(ModelTestStrictBroken), typeof(ModelTestDegradedBroken), typeof(ModelTestShipment), typeof(ModelTestParcel), typeof(ModelTestGrouped), typeof(ModelTestTicket), typeof(ModelTestBanded), typeof(ModelTestTwoBands), typeof(ModelTestNestedBands), typeof(ModelTestTiedBands), typeof(ModelTestServiceOrder), typeof(ModelTestLaterRemoval),
                         typeof(ModelTestStyled), typeof(ModelTestAppearanceClashStrict), typeof(ModelTestAppearanceClashDegraded), typeof(ModelTestAppearanceBroken), typeof(ModelTestAppearanceExport),
                         typeof(ModelTestAppearanceOnBrokenLayout), typeof(ModelTestAppearanceBadCriteriaDegraded), typeof(ModelTestAppearanceOverridden) })
                AdditionalExportedTypes.Add(type);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApplicationModelCollection : ICollectionFixture<ApplicationModelFixture> {
    public const string Name = "Application model";
}
