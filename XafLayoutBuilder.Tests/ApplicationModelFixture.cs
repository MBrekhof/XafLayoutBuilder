using DevExpress.ExpressApp;
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

    public ApplicationModelFixture() {
        // A layout error throws where the model is read instead of going to the trace log.
        XafLayoutBuilderModule.FailFastOnLayoutErrors = true;
        var factory = new DesignerModelFactory();
        var module = new ModelTestModule();
        Model = factory.CreateApplicationModel(module, factory.CreateModulesManager(module, AppContext.BaseDirectory), ModelStoreBase.Empty);

        // The two broken views are read here, before any test runs. A layout is generated once, and anything that walks
        // the model first would generate them under whatever the switch happens to be at that moment: xUnit's failure
        // message for an assertion over model nodes prints them property by property, which generated the strict view's
        // layout inside the formatter and swallowed its exception.
        StrictBrokenLayoutError = Record.Exception(() => Class<ModelTestStrictBroken>().DefaultDetailView.Layout.Count);
        XafLayoutBuilderModule.FailFastOnLayoutErrors = false;
        try {
            DegradedBrokenLayoutIds = LayoutIds(Class<ModelTestDegradedBroken>().DefaultDetailView.Layout).ToList();
        }
        finally {
            XafLayoutBuilderModule.FailFastOnLayoutErrors = true;
        }
    }

    public IModelClass Class<T>() => Model.BOModel.GetClass(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not in the BOModel; test types must be top-level public [DomainComponent] classes.");

    /// <summary>Every group, tab and item id in a layout, depth first.</summary>
    public static IEnumerable<string> LayoutIds(IEnumerable<IModelViewLayoutElement> elements) =>
        elements.SelectMany(e => e switch {
            IModelTabbedGroup t => LayoutIds(t).Prepend(t.Id),
            IModelLayoutGroup g => LayoutIds(g).Prepend(g.Id),
            _ => [e.Id],
        });

    public void Dispose() => XafLayoutBuilderModule.FailFastOnLayoutErrors = failFast;

    sealed class ModelTestModule : ModuleBase {
        public ModelTestModule() {
            RequiredModuleTypes.Add(typeof(SystemModule));
            RequiredModuleTypes.Add(typeof(XafLayoutBuilderModule));
            foreach (var type in new[] { typeof(ModelTestCustomer), typeof(ModelTestLine), typeof(ModelTestOrder), typeof(ModelTestContact),
                         typeof(ModelTestStrictBroken), typeof(ModelTestDegradedBroken) })
                AdditionalExportedTypes.Add(type);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApplicationModelCollection : ICollectionFixture<ApplicationModelFixture> {
    public const string Name = "Application model";
}
