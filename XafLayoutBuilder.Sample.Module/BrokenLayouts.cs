using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;
using XafLayoutBuilder.Sample.Module.BusinessObjects;

namespace XafLayoutBuilder.Sample.Module;

/// <summary>
/// Test fixture for the startup diagnostics (start document, session 5). Registers three failures: a Customer layout
/// that places InternalCode, which is [Browsable(false)] and therefore has no view item (XLB001), and on Order both a
/// detail factory whose Build() places a member twice and columns that list the Lines collection (XLB003). Wired to the
/// host's --break-layout argument; the E2E gate asserts all of them are reported in one startup with fail-fast on, and
/// logged while the host serves with it off.
/// </summary>
public static class BrokenLayouts {
    public static void Register() {
        LayoutRegistry.Register<Customer>(
            LayoutBuilder<Customer>.Create()
                // The caption is a canary: it can only appear if the rejected spec was partly applied (GATE-001).
                .Group("Main", g => g.Caption("Broken layout").Item(x => x.Name).Item(x => x.City).Item(x => x.InternalCode))
                .Build(),
            columns: null);
        // Two independent failures on one type. The factories defer the throwing Build() into the fail-fast policy
        // (REG-001), and the startup check has to report the DetailView's failure and the ListView's alike (RESOLVE-002).
        LayoutRegistry.Register<Order>(
            detail: () => LayoutBuilder<Order>.Create().Group("Main", g => g.Item(x => x.Number).Item(x => x.Number)).Build(),
            // "Broken number" is the same kind of canary, set on a column the updater reaches before the failing one.
            columns: () => ListViewColumnsBuilder<Order>.Create().Column(x => x.Number, caption: "Broken number").Column(x => x.Lines).Build());
    }
}
