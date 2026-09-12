using System.Linq.Expressions;
using System.Reflection;

namespace XafLayoutBuilder.Core;

internal static class MemberPath {
    /// <summary>x => x.Customer → "Customer". Anything else (x => x.Customer.Name, method calls) is rejected.</summary>
    public static string Of<T>(Expression<Func<T, object?>> member) {
        var body = member.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary) body = unary.Operand; // value types get boxed
        if (body is MemberExpression { Expression: ParameterExpression, Member: PropertyInfo or FieldInfo } m) return m.Member.Name;
        throw new LayoutSpecException(
            $"'{member}' is not a simple member access. Use x => x.Member; nested paths (x => x.Customer.Name) are for columns only.");
    }

    /// <summary>
    /// x => x.Customer.City → "Customer.City": property or field accesses down to the parameter, the dotted path XAF's own
    /// generator gives a column over a reference's member. For columns only. Method calls and casts inside the chain are
    /// rejected.
    /// </summary>
    public static string ChainOf<T>(Expression<Func<T, object?>> member) {
        var body = member.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary) body = unary.Operand; // value types get boxed
        var segments = new Stack<string>();
        while (body is MemberExpression { Member: PropertyInfo or FieldInfo } m) {
            segments.Push(m.Member.Name);
            body = m.Expression;
        }
        if (body is ParameterExpression && segments.Count > 0) return string.Join(".", segments);
        throw new LayoutSpecException($"'{member}' is not a member access. Use x => x.Member, or x => x.Reference.Member for a column.");
    }
}
