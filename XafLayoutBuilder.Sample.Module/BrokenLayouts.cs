using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;
using XafLayoutBuilder.Sample.Module.BusinessObjects;

namespace XafLayoutBuilder.Sample.Module;

/// <summary>
/// Test fixture for the startup diagnostics (start document, session 5): registers a layout that places
/// Customer.InternalCode, which is [Browsable(false)] and therefore has no view item. Wired to the host's
/// --break-layout argument; the E2E gate starts the app with it and asserts XLB001 is reported at startup.
/// </summary>
public static class BrokenLayouts {
    public static void Register() =>
        LayoutRegistry.Register<Customer>(
            LayoutBuilder<Customer>.Create()
                .Group("Main", g => g.Item(x => x.Name).Item(x => x.City).Item(x => x.InternalCode))
                .Build(),
            columns: null);
}
