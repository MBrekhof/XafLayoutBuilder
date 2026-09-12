using XafLayoutBuilder.Core;
using XafLayoutBuilder.Module;
using XafLayoutBuilder.Sample.Module.BusinessObjects;

namespace XafLayoutBuilder.Sample.Module;

/// <summary>Valid layouts the E2E gate swaps in with a command-line switch. <see cref="BrokenLayouts"/> holds the invalid ones.</summary>
public static class GateFixtures {
    /// <summary>
    /// --extra-column: Order's own columns plus Notes, as if a property had been added to the class after an administrator
    /// froze the column set (FREEZE-001). The DetailView layout stays the one in Order.Layout.cs.
    /// </summary>
    public static void RegisterExtraOrderColumn() =>
        LayoutRegistry.Register<Order>(
            detail: Order.BuildDetailViewLayout,
            columns: () => Order.BuildListViewColumns() is { } columns
                ? columns with { Columns = [.. columns.Columns, new ColumnSpec(nameof(Order.Notes))] }
                : null);
}
