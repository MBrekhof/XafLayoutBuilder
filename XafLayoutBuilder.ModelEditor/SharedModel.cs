using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Utils;
using DevExpress.ExpressApp.AmbientContext;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// Where a host keeps its shared (administrator) model differences: the persistent <see cref="IModelDifference"/> type and
/// the context id it gives its <see cref="ModelDifferenceDbStore"/>, "Blazor" in the template (dxdocs 113698). Set on
/// <see cref="XafModelEditorModule.SharedDifferences"/>.
/// </summary>
public sealed record SharedDifferenceStoreSettings(Type ModelDifferenceType, string ContextId);

/// <summary>
/// MODELEDITOR-010: the shared (administrator) differences, stored in the database without a user (UserId "") below every
/// user's own. XAF Blazor loads them once, into the shared warmed-up model every circuit builds on (ApplicationWarmUpService
/// .RunWarmUpWithModel, SharedApplicationProvider), so the running application cannot write to them and does not see a
/// change until it restarts. The editor edits them in a model of its own (<see cref="SharedModelSession"/>) and saves through
/// the shared store; the module layers the same store over every circuit's model at logon (ExtraDiffStores,
/// XafApplication.LoadUserDifferences 1485-1519), so a saved change shows to everyone at their next page load.
/// </summary>
public static class SharedModel {
    /// <summary>
    /// The host's shared store, bound to the given application; null when the host has not said where it keeps them. The
    /// store loaded under every user's circuit reads without security (<paramref name="secured"/> false): a role with only
    /// the template's own-record permission does not get the shared record back from a secured query, and the store would
    /// then try to create a second one and fail the logon (docs/api-notes.md). XAF's shared application loads the layer
    /// without a user too. The editing store stays secured, so the save's permission checks apply.
    /// </summary>
    public static ModelDifferenceDbStore? CreateStore(XafApplication application, bool secured = true) {
        if (XafModelEditorModule.SharedDifferences is not { } settings) return null;
        var store = new ModelDifferenceDbStore(application, settings.ModelDifferenceType, true, settings.ContextId);
        if (!secured) store.CreateObjectSpaceHandler = NonsecuredObjectSpace;
        return store;
    }

    /// <summary>
    /// Whether the user may edit the shared differences: the model editing permission, and the write the store itself requires
    /// on the shared record (ModelDifferenceDbStore.SaveDifference, ModelDifferenceDbStore.cs 178-213; a create when there is
    /// none yet). The template's default role writes only its own record ("UserId = ToStr(CurrentUserId())"), an administrative
    /// role every one. An application without a request security system allows it, as Edit Model does.
    /// </summary>
    public static bool CanEdit(XafApplication application) {
        if (XafModelEditorModule.SharedDifferences is not { } settings || !ModelEditorController.CanEditModel(application)) return false;
        if (application.Security is not IRequestSecurity security) return true;
        // Whether the record exists is asked without security: a secured query hides it from a role that may not read it, and
        // such a role must not be offered a create in its place (Codex review).
        using var any = NonsecuredObjectSpace(application, settings.ModelDifferenceType);
        var exists = ModelDifferenceDbStore.FindModelDifference(any, settings.ModelDifferenceType, "", settings.ContextId) is not null;
        using var objectSpace = application.CreateObjectSpace(settings.ModelDifferenceType);
        var shared = ModelDifferenceDbStore.FindModelDifference(objectSpace, settings.ModelDifferenceType, "", settings.ContextId);
        // The store writes one aspect row per language and creates the rows it lacks (ModelDifferenceDbStore.cs 194-213), so
        // those need Write and Create too (Codex review 2).
        var aspectType = ModelDifferenceDbStore.GetModelDifferenceAspectType(application.TypesInfo, settings.ModelDifferenceType);
        if (!security.IsGranted(new PermissionRequest(objectSpace, aspectType, SecurityOperations.Create))) return false;
        if (shared is not null)
            return security.IsGranted(new PermissionRequest(objectSpace, settings.ModelDifferenceType, SecurityOperations.Write, shared))
                && shared.Aspects.All(aspect => security.IsGranted(new PermissionRequest(objectSpace, aspectType, SecurityOperations.Write, aspect)));
        return !exists && security.IsGranted(new PermissionRequest(objectSpace, settings.ModelDifferenceType, SecurityOperations.Create));
    }

    /// <summary>
    /// Whether the stored shared record holds what the layer holds: one row per aspect with XML, written with the store's
    /// header (ModelDifferenceDbStore.cs 194-213). <paramref name="storedXml"/> gives the stored row's XML for an aspect name,
    /// null for no row. The database store refuses a save silently, with a logged warning, when a permission is missing or a
    /// newer version is stored (178-186), so the editor checks after saving instead of trusting it (Codex review 2).
    /// </summary>
    public static bool StoredHolds(ModelApplicationBase layer, Func<string, string?> storedXml) {
        var writer = new ModelXmlWriter();
        for (var i = 0; i < layer.AspectCount; i++) {
            var xml = writer.WriteToString(layer, i);
            var stored = storedXml(layer.GetAspect(i));
            // An aspect the edits emptied has no row, or the blank row StoredAspectCleanup leaves; a row still holding the old
            // differences means the save was refused (Codex review 3).
            if (string.IsNullOrEmpty(xml)) {
                if (stored is not null && stored != ModelDifferenceDbStore.EmptyXafml) return false;
                continue;
            }
            if (stored != $"{ModelDifferenceDbStore.XafmlHeader}{Environment.NewLine}{xml}") return false;
        }
        return true;
    }

    /// <summary>The stored rows of the shared record, aspect name to XML; empty when there is no record.</summary>
    public static IReadOnlyDictionary<string, string?> StoredRows(IModelDifference? record) =>
        record?.Aspects.ToDictionary(a => a.Name ?? "", a => (string?)a.Xml) ?? new Dictionary<string, string?>();

    /// <summary>Whether the stored rows differ from a snapshot: another administrator saved meanwhile (Codex review 3).</summary>
    public static bool RowsChanged(IReadOnlyDictionary<string, string?> snapshot, IReadOnlyDictionary<string, string?> now) =>
        snapshot.Count != now.Count || snapshot.Any(pair => !now.TryGetValue(pair.Key, out var xml) || xml != pair.Value);

    internal static IObjectSpace NonsecuredObjectSpace(XafApplication application, Type type) =>
        application.ServiceProvider?.GetService(typeof(IObjectSpaceFactoryBase)) is IObjectSpaceFactoryBase factory
            ? factory.CreateNonSecuredObjectSpace(type)
            : application.CreateObjectSpace(type);

    /// <summary>Opens a session over the application's shared differences, in the application's current language.</summary>
    /// <param name="preload">Differences to read into the shared layer first (MODELEDITOR-009, ModelEditing.MergeInto).</param>
    public static SharedModelSession Open(XafApplication application, Action<ModelApplicationBase>? preload = null) {
        var store = CreateStore(application) ?? throw new InvalidOperationException("XafModelEditorModule.SharedDifferences is not set.");
        return new SharedModelSession(((IApplicationModelManagerProvider)application).GetModelManager(), store,
            ((ModelApplicationBase)application.Model).CurrentAspect, application, preload);
    }
}

/// <summary>
/// A model whose writable layer is the shared differences: the application's unchangeable model (generated, module and
/// administrator layers, as the circuit's own model builds on it) with a fresh layer loaded from the shared store on top,
/// built by the application's own manager (IApplicationModelManagerProvider, XafApplication.cs 2662).
/// <para>
/// Every access runs inside <see cref="Enter"/>: the shared unchangeable layer resolves which model is its master through
/// the circuit's value-manager storage (BlazorModelMultipleMasterStore, ModelNode.MasterItem 350-360), so a second model
/// built in that storage would take the circuit model's place. This session keeps a storage of its own, the way XAF builds
/// its shared application in one (SharedApplicationCreator.CreateSharedApplication, IValueManagerStorageContext), and
/// swaps it in for the duration of a synchronous read or write. Nothing may await inside.
/// </para>
/// </summary>
public sealed class SharedModelSession : IDisposable {
    readonly Storage storage = new();
    readonly ModelDifferenceStore store;
    readonly ModelApplicationBase layer;
    readonly XafApplication? application;

    public ModelApplicationBase Model { get; }

    /// <summary>The shared layer's stored version, the guard the store's save applies (ModelDifferenceDbStore.cs 181).</summary>
    public int Version => layer.Version;

    /// <summary>The store the session saves through.</summary>
    public ModelDifferenceStore Store => store;

    /// <param name="application">The application whose database the store writes, for the check after a save; null skips it (the tests).</param>
    /// <param name="preload">MODELEDITOR-009: reads more differences into the shared layer before it joins the model, as a
    /// store loads them (ModelEditing.MergeInto); it must not touch the circuit's model, whose storage is swapped out here.</param>
    public SharedModelSession(ApplicationModelManager manager, ModelDifferenceStore store, string currentAspect, XafApplication? application = null,
        Action<ModelApplicationBase>? preload = null) {
        this.store = store;
        this.application = application;
        using (Enter()) {
            // Just the store's differences: a node added to the layer beforehand, as XafApplication.LoadUserDifferences adds
            // Options and Views to the user layer (1503-1511), made the warmed-up unchangeable layer reset a node it must not.
            // Differences read from XML are what the store itself gives the layer.
            layer = manager.CreateLayerByStore("SharedDiff", store);
            preload?.Invoke(layer);
            Model = manager.CreateModelApplication([layer]);
            // Collapsed as XAF collapses every circuit's model (XafApplication.SetupModelApplication 491-505): values come from a
            // cache, and a reset shows its old value until the reload after Save, as in the user model (MODELEDITOR-002).
            Model.Collapse();
            Model.SetCurrentAspect(currentAspect);
            // Captions are read through the caption helper of the current storage (CaptionHelperImplementer.cs 55); outside a
            // value-manager context (the tests) the helper is process-wide and stays the application's.
            if (ValueManagerContext.IsActive) CaptionHelper.Setup((IModelApplication)Model);
        }
        snapshot = ReadStoredRows();
    }

    /// <summary>
    /// Makes this session's storage the current one until disposed; a no-op outside a value-manager context (the tests). The
    /// previous storage is put back explicitly: ValueManagerContext.OverrideStorage's own scope does not restore it
    /// (ValueManagerContext.cs 55-66, 91-97).
    /// </summary>
    public IDisposable Enter() => ValueManagerContext.IsActive ? new StorageScope(storage) : NoScope.Instance;

    /// <summary>
    /// MODELEDITOR-009: whether the session's model holds a node of these ids. A merged difference for a node the shared model
    /// lost since the user's circuit was built would be unusable, and the user's own copy is reset right after (Codex plan review 2).
    /// </summary>
    public bool HasNode(IReadOnlyList<string> ids) => Run(() => {
        IModelNode? node = Model;
        foreach (var id in ids) {
            if ((node = node.GetNode(id)) is null) return false;
        }
        return true;
    });

    /// <summary>
    /// The first of the merged nodes the session's model does not hold, as a path; null when it holds them all. A difference
    /// for a node the shared model lost since the user's circuit was built, at any depth, is dropped as unusable when the
    /// model is built, and the user's copy is reset right after the merge (Codex diff review).
    /// </summary>
    public string? FirstMissing(IEnumerable<IReadOnlyList<string>> nodes) =>
        nodes.FirstOrDefault(ids => !HasNode(ids)) is { } missing ? string.Join("/", missing) : null;

    public void Run(Action action) {
        using (Enter()) action();
    }

    public T Run<T>(Func<T> function) {
        using (Enter()) return function();
    }

    // The stored rows when the session opened, for the database store; null otherwise.
    IReadOnlyDictionary<string, string?>? snapshot;

    // ponytail: one shared save at a time per process, so the row check, the save, the cleanup and the version bump are one
    // step and two administrators cannot interleave them (Codex review 5); the record's types carry OptimisticLockIgnore, so
    // the database does not do it. A host on several servers needs a database lock here instead.
    static readonly object SaveGate = new();

    /// <summary>
    /// <see cref="Save"/>, the given cleanup of emptied aspect rows (StoredAspectCleanup) and <see cref="Verify"/> as one
    /// step, serialized process-wide.
    /// </summary>
    public void Persist(Action cleanup) {
        lock (SaveGate) {
            Save();
            cleanup();
            Verify();
        }
    }

    IReadOnlyDictionary<string, string?>? ReadStoredRows() {
        if (store is not ModelDifferenceDbStore || application is null || XafModelEditorModule.SharedDifferences is not { } settings) return null;
        using var objectSpace = SharedModel.NonsecuredObjectSpace(application, settings.ModelDifferenceType);
        return SharedModel.StoredRows(ModelDifferenceDbStore.FindModelDifference(objectSpace, settings.ModelDifferenceType, "", settings.ContextId));
    }

    /// <summary>
    /// Writes the shared layer through the store. First the stored rows are compared with those read when the session opened:
    /// the store replaces every row, so another administrator's save meanwhile would be overwritten in silence (Codex review
    /// 3). Call <see cref="Verify"/> after the emptied aspects are blanked.
    /// </summary>
    public void Save() {
        if (snapshot is not null && ReadStoredRows() is { } now && SharedModel.RowsChanged(snapshot, now))
            throw new InvalidOperationException("The shared model changed since you opened it: someone else saved. " +
                "Reload the page to see it, then make your edits again.");
        using (Enter()) store.SaveDifference(layer);
        // The rows are this session's own now: a retry after a failed cleanup must not read them as someone else's (Codex review 4).
        snapshot = ReadStoredRows();
    }

    /// <summary>
    /// Checks that the record holds the layer, emptied aspects included: the database store refuses silently when the user may
    /// not write the record or an aspect, or a newer version is stored (ModelDifferenceDbStore.cs 178-213). Throws then, so the
    /// edits stay in the editor (Codex review 2). Then the record's Version is raised, as the Administrative UI raises it
    /// (ModelDifferenceViewController.cs 188-196): another editor opened before this save holds the old version, and the
    /// store's own guard refuses its save (181), which its Verify reports. That closes the window between two editors' row
    /// checks to the moment between this save and this bump (Codex review 4).
    /// </summary>
    public void Verify() {
        if (ReadStoredRows() is not { } rows) return;
        if (!Run(() => SharedModel.StoredHolds(layer, aspect => rows.GetValueOrDefault(aspect))))
            throw new InvalidOperationException("The shared model was not saved: your role may not write the shared record or one of its aspects, " +
                "or someone stored a newer version meanwhile. Your edits are still here; reload the page and try again, or ask an administrator.");
        if (application is not null && XafModelEditorModule.SharedDifferences is { } settings) {
            using var objectSpace = SharedModel.NonsecuredObjectSpace(application, settings.ModelDifferenceType);
            if (ModelDifferenceDbStore.FindModelDifference(objectSpace, settings.ModelDifferenceType, "", settings.ContextId) is { } record) {
                record.Version++;
                objectSpace.CommitChanges();
                layer.Version = record.Version;
            }
        }
        snapshot = rows;
    }

    public void Dispose() {
        using (Enter()) {
            while (Model.LayersCount > 0) ModelApplicationHelper.RemoveLayer(Model);
        }
    }

    sealed class StorageScope : IDisposable {
        readonly IValueManagerStorage? previous = ValueManagerContext.Storage;

        public StorageScope(IValueManagerStorage storage) => ValueManagerContext.OverrideStorage(storage);

        public void Dispose() {
            if (previous is not null) ValueManagerContext.OverrideStorage(previous);
        }
    }

    sealed class NoScope : IDisposable {
        public static readonly NoScope Instance = new();
        public void Dispose() { }
    }

    // XAF's own storage class is internal; the interface is three methods over a dictionary.
    sealed class Storage : IValueManagerStorage {
        readonly Dictionary<string, object?> values = [];

        public bool TryGetValue<T>(string key, out T? value) {
            if (values.TryGetValue(key, out var found) && found is T typed) {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void SetValue<T>(string key, T? value) => values[key] = value;

        public void RemoveValue(string key) => values.Remove(key);
    }
}
