using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace XafLayoutBuilder.ModelEditor;

/// <summary>
/// Renders its content inside a scope: the child content's expressions run in this component's BuildRenderTree, so a
/// shared model session's storage is current while the editor's markup reads the model (MODELEDITOR-010). The DevExpress
/// components inside render on their own, outside the scope, and read no model.
/// </summary>
public sealed class IsolatedRender : ComponentBase {
    [Parameter] public Func<IDisposable>? Scope { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) {
        using (Scope?.Invoke()) ChildContent?.Invoke(builder);
    }
}
