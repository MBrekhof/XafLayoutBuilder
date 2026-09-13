using System.Runtime.CompilerServices;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Model.Core;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// Works around the database store keeping an aspect whose differences became empty: ModelDifferenceDbStore.SaveDifference
/// writes only aspects with XML (ModelDifferenceDbStore.cs 194-213), so after a reset of the last value of an aspect (a
/// localized caption, say) the stored row is neither cleared nor deleted and the next load brings the value back
/// (docs/devexpress-support-request.md item 7). After the editor saves, it blanks those rows. Public API only:
/// FindModelDifference, FindModelDifferenceAspect, EmptyXafml and the store's own CreateObjectSpaceHandler.
/// </summary>
internal static class StoredAspectCleanup {
    static readonly ConditionalWeakTable<XafApplication, ModelDifferenceDbStore> Stores = new();

    /// <summary>
    /// Remembers the application's user-differences store whenever XAF asks for it (every SaveModelChanges,
    /// XafApplication.cs 2497-2506). Subscribe after setup is complete, so this handler runs after the host's module set
    /// the store; the XAF Blazor template does that in its module's Setup.
    /// </summary>
    public static void Track(XafApplication application) =>
        application.CreateCustomUserModelDifferenceStore += (sender, e) => {
            if (sender is XafApplication app && e.Store is ModelDifferenceDbStore store) Stores.AddOrUpdate(app, store);
        };

    /// <summary>
    /// Blanks the stored rows of the aspects this save emptied (<see cref="ModelEditing.AspectsEmptiedBy"/>). Only those: the
    /// circuit's model can be stale, and an aspect empty in it may hold what another tab saved since (Codex re-review).
    /// Call after SaveModelChanges.
    /// </summary>
    public static void ClearEmptied(XafApplication application, IReadOnlyCollection<string> emptied) {
        if (emptied.Count == 0
            || ((ModelApplicationBase)application.Model).LastLayer is not { } userLayer
            || !Stores.TryGetValue(application, out var store)
            || application.Security?.UserId is not { } userId) return;
        // ponytail: the store's ModelDifferenceType is internal, so the application's one persistent IModelDifference class
        // stands in for it; with more than one the cleanup is skipped (the stale row stays until someone clears it).
        var types = application.TypesInfo.PersistentTypes
            .Where(t => t.IsPersistent && !t.IsAbstract && typeof(IModelDifference).IsAssignableFrom(t.Type))
            .ToList();
        if (types.Count != 1) return;
        var type = types[0].Type;
        using var objectSpace = store.CreateObjectSpaceHandler(application, type);
        var userIdText = ModelDifferenceDbStore.UserIdTypeConverter.ConvertToInvariantString(userId) ?? "";
        if (ModelDifferenceDbStore.FindModelDifference(objectSpace, type, userIdText, store.ContextId) is not { } difference
            || !StoreAcceptsSaveFrom(difference, userLayer.Version)) return;
        var changed = false;
        foreach (var aspect in emptied) {
            if (ModelDifferenceDbStore.FindModelDifferenceAspect(difference, aspect) is { } row && row.Xml != ModelDifferenceDbStore.EmptyXafml) {
                row.Xml = ModelDifferenceDbStore.EmptyXafml;
                changed = true;
            }
        }
        if (changed) objectSpace.CommitChanges();
    }

    /// <summary>
    /// The guard SaveDifference applies (ModelDifferenceDbStore.cs 181): a stored difference newer than the model being saved,
    /// one an administrator copied to the user while this circuit was open, say, is not overwritten (Codex review).
    /// </summary>
    internal static bool StoreAcceptsSaveFrom(IModelDifference difference, int layerVersion) => difference.Version <= layerVersion;
}
