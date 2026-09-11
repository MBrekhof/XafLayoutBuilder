using System.Globalization;
using System.Text;

namespace XafLayoutBuilder.Core;

/// <summary>
/// Prints a spec as the fluent C# from the start document, one call per line, so a diff against the
/// existing builder code is readable. Round trip: builder -> spec -> Print gives the builder back.
/// </summary>
public static class CSharpLayoutPrinter {
    /// <summary>The whole partial class: both static members, `null` for a missing spec.</summary>
    public static string PrintClass(string typeName, DetailLayoutSpec? detail, ListColumnsSpec? columns, IEnumerable<string>? notes = null) {
        var sb = new StringBuilder();
        sb.AppendLine("using XafLayoutBuilder.Core;");
        sb.AppendLine();
        foreach (var n in notes ?? []) sb.Append("// ").AppendLine(n);
        sb.Append("public partial class ").Append(typeName).AppendLine(" : ISupportViewLayoutCustomization {");
        sb.AppendLine("    public static DetailLayoutSpec? BuildDetailViewLayout() =>");
        sb.AppendLine(detail is null ? "        null;" : Indent(PrintDetail(detail, typeName), 8) + ";");
        sb.AppendLine();
        sb.AppendLine("    public static ListColumnsSpec? BuildListViewColumns() =>");
        sb.AppendLine(columns is null ? "        null;" : Indent(PrintColumns(columns, typeName), 8) + ";");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>The builder expression only, unindented, without trailing semicolon.</summary>
    public static string PrintDetail(DetailLayoutSpec spec, string typeName) {
        var sb = new StringBuilder();
        sb.Append("LayoutBuilder<").Append(typeName).Append(">.Create()");
        foreach (var node in spec.Nodes) PrintNode(sb, node, 1);
        foreach (var hidden in spec.HiddenMembers) sb.AppendLine().Append(Pad(1)).Append(".Hide(x => x.").Append(hidden).Append(')');
        sb.AppendLine().Append(Pad(1)).Append(".Build()");
        return sb.ToString();
    }

    /// <summary>The builder expression only, unindented, without trailing semicolon.</summary>
    public static string PrintColumns(ListColumnsSpec spec, string typeName) {
        var sb = new StringBuilder();
        sb.Append("ListViewColumnsBuilder<").Append(typeName).Append(">.Create()");
        PrintColumnCalls(sb, spec, 1);
        if (spec.Lookup is { } lookup) {
            sb.AppendLine().Append(Pad(1)).Append(".Lookup(l => l");
            PrintColumnCalls(sb, lookup, 2);
            sb.Append(')');
        }
        sb.AppendLine().Append(Pad(1)).Append(".Build()");
        return sb.ToString();
    }

    static void PrintColumnCalls(StringBuilder sb, ListColumnsSpec spec, int depth) {
        foreach (var c in spec.Columns) {
            sb.AppendLine().Append(Pad(depth)).Append(".Column(x => x.").Append(c.Member);
            if (c.Width is { } w) sb.Append(", width: ").Append(w);
            if (c.SortOrder != ColumnSortOrder.None) sb.Append(", sort: ColumnSortOrder.").Append(c.SortOrder);
            if (c.Caption is not null) sb.Append(", caption: ").Append(Quote(c.Caption));
            sb.Append(')');
        }
        foreach (var hidden in spec.HiddenMembers) sb.AppendLine().Append(Pad(depth)).Append(".Hide(x => x.").Append(hidden).Append(')');
    }

    static void PrintNode(StringBuilder sb, LayoutNodeSpec node, int depth) {
        switch (node) {
            case LayoutItemSpec item:
                sb.AppendLine().Append(Pad(depth)).Append(".Item(x => x.").Append(item.Member);
                if (item.RelativeSize is { } size) sb.Append(", relativeSize: ").Append(Num(size));
                sb.Append(')');
                break;
            case LayoutGroupSpec g:
                sb.AppendLine().Append(Pad(depth)).Append(".Group(").Append(Quote(g.Id)).Append(", g => g");
                PrintGroupBody(sb, g, depth + 1);
                sb.Append(')');
                break;
            case TabbedGroupSpec t:
                sb.AppendLine().Append(Pad(depth)).Append(".Tabs(").Append(Quote(t.Id)).Append(", t => t");
                foreach (var tab in t.Tabs) PrintTab(sb, tab, depth + 1);
                sb.Append(')');
                break;
        }
    }

    static void PrintGroupBody(StringBuilder sb, LayoutGroupSpec g, int depth) {
        if (g.Caption is not null) sb.AppendLine().Append(Pad(depth)).Append(".Caption(").Append(Quote(g.Caption)).Append(')');
        if (g.Direction == FlowDirection.Horizontal) sb.AppendLine().Append(Pad(depth)).Append(".Flow(FlowDirection.Horizontal)");
        if (g.Collapsible) sb.AppendLine().Append(Pad(depth)).Append(".Collapsible()");
        if (g.RelativeSize is { } size) sb.AppendLine().Append(Pad(depth)).Append(".RelativeSize(").Append(Num(size)).Append(')');
        if (g.ImageName is not null) sb.AppendLine().Append(Pad(depth)).Append(".Image(").Append(Quote(g.ImageName)).Append(')');
        foreach (var child in g.Children) PrintNode(sb, child, depth);
    }

    // TabFor(member) is a tab group whose id is the member and whose only child is that member's item.
    static void PrintTab(StringBuilder sb, LayoutGroupSpec tab, int depth) {
        if (tab.Children.Count == 1 && tab.Children[0] is LayoutItemSpec { RelativeSize: null } only && only.Member == tab.Id
            && !tab.Collapsible && tab.RelativeSize is null && tab.Direction == FlowDirection.Vertical) {
            sb.AppendLine().Append(Pad(depth)).Append(".TabFor(x => x.").Append(tab.Id);
            if (tab.ImageName is not null) sb.Append(", imageName: ").Append(Quote(tab.ImageName));
            if (tab.Caption is not null) sb.Append(", caption: ").Append(Quote(tab.Caption));
            sb.Append(')');
            return;
        }
        sb.AppendLine().Append(Pad(depth)).Append(".Tab(").Append(Quote(tab.Id)).Append(", g => g");
        PrintGroupBody(sb, tab, depth + 1);
        sb.Append(')');
    }

    static string Pad(int depth) => new(' ', depth * 4);
    static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);
    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string Indent(string text, int spaces) {
        var pad = new string(' ', spaces);
        return string.Join(Environment.NewLine, text.Split('\n').Select(l => pad + l.TrimEnd('\r')));
    }
}
