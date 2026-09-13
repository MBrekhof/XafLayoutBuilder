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
    public XafModelEditorModule() {
        AdditionalExportedTypes.Add(typeof(ModelEditorWindow));
    }
}

/// <summary>What the popup shows. The one property exists to host <see cref="ModelEditorPropertyEditor"/>.</summary>
[DomainComponent]
public class ModelEditorWindow : NonPersistentBaseObject {
    [EditorAlias(ModelEditorPropertyEditor.Alias)]
    public virtual string? Model { get; set; }
}
