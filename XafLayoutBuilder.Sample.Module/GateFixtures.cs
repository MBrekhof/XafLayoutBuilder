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

    /// <summary>
    /// --nested-column: Order's own columns plus the customer's city, a column over a reference's member (NEST-001), named
    /// with the dotted path XAF's own generator gives such a column. The DetailView layout stays the one in Order.Layout.cs.
    /// </summary>
    public static void RegisterNestedOrderColumn() =>
        LayoutRegistry.Register<Order>(
            detail: Order.BuildDetailViewLayout,
            columns: () => Order.BuildListViewColumns() is { } columns
                ? columns with { Columns = [.. columns.Columns, new ColumnSpec($"{nameof(Order.Customer)}.{nameof(Customer.City)}")] }
                : null);
}
