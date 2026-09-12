using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;
using XafLayoutBuilder.Sample.Module.BusinessObjects;

namespace XafLayoutBuilder.Sample.Module;

/// <summary>
/// Test fixture for the startup diagnostics (start document, session 5). Registers two independent failures:
/// a layout that places Customer.InternalCode, which is [Browsable(false)] and therefore has no view item (XLB001),
/// and Order columns that list the Lines collection (XLB003). Wired to the host's --break-layout argument; the E2E
/// gate starts the app with it and asserts both are reported in the same startup.
/// </summary>
public static class BrokenLayouts {
    public static void Register() {
        LayoutRegistry.Register<Customer>(
            LayoutBuilder<Customer>.Create()
                .Group("Main", g => g.Item(x => x.Name).Item(x => x.City).Item(x => x.InternalCode))
                .Build(),
            columns: null);
        LayoutRegistry.Register<Order>(
            detail: null,
            ListViewColumnsBuilder<Order>.Create().Column(x => x.Number).Column(x => x.Lines).Build());
    }
}
