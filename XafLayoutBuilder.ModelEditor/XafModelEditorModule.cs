using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// MODELEDITOR-001 spike: "Edit Model" in the Tools tab opens the running application's model as a tree with a value
/// grid, for users whose role may edit the model. Edits go to the current user's differences only. Add it to a Blazor
/// host's modules; it does not depend on the layout builder.
/// </summary>
public sealed class XafModelEditorModule : ModuleBase {
    /// <summary>
    /// MODELEDITOR-010: where the host keeps its shared (administrator) differences, the same type and context id it gives
    /// its ModelDifferenceDbStore. Set before the application starts. With it, "Edit Shared Model" appears for users who
    /// may write the shared record (<see cref="SharedModel.CanEdit"/>), and every circuit layers the stored shared differences
    /// over its model at logon, so a saved shared edit shows to everyone at their next page load without a restart. Null,
    /// the default: no shared editing.
    /// <para>
    /// Leave the template's <c>CreateCustomModelDifferenceStore</c> subscription commented out: this layer is then the one
    /// source of the shared differences and Model.xafml their baseline (the store imports it into the record once). A host
    /// that also registers the database store there bakes the record's startup values into the warmed-up model below this
    /// layer, and a shared value reset in the editor shows again until the host restarts (Codex review).
    /// </para>
    /// </summary>
    public static SharedDifferenceStoreSettings? SharedDifferences { get; set; }

    public XafModelEditorModule() {
        AdditionalExportedTypes.Add(typeof(ModelEditorWindow));
    }

    public override void Setup(XafApplication application) {
        base.Setup(application);
        // After every module's Setup, so the handler sees the user-differences store the host's module set.
        application.SetupComplete += (_, _) => StoredAspectCleanup.Track(application);
        // The shared store as an extra layer below the user layer, read again for every circuit (XafApplication.LoadUserDifferences
        // 1492-1496): the warmed-up shared model holds the administrator differences as they were at startup only. Read
        // without security, as XAF's shared application reads them: every user gets them, whatever the role may query.
        application.CreateCustomUserModelDifferenceStore += (sender, e) => {
            if (sender is XafApplication app && SharedModel.CreateStore(app, secured: false) is { } store) e.AddExtraDiffStore("SharedDiff", store);
        };
    }
}

/// <summary>What the popup shows. <see cref="Model"/> exists to host <see cref="ModelEditorPropertyEditor"/>.</summary>
[DomainComponent]
public class ModelEditorWindow : NonPersistentBaseObject {
    [EditorAlias(ModelEditorPropertyEditor.Alias)]
    public virtual string? Model { get; set; }

    /// <summary>View in Model: the id of the view whose node the editor opens on (MODELEDITOR-005).</summary>
    [System.ComponentModel.Browsable(false)]
    public virtual string? StartViewId { get; set; }

    /// <summary>MODELEDITOR-010: the editor edits the shared (administrator) differences instead of the user's own.</summary>
    [System.ComponentModel.Browsable(false)]
    public virtual bool Shared { get; set; }
}
