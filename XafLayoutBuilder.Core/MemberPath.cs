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
            $"'{member}' is not a simple member access. Use x => x.Member; nested paths (x => x.Customer.Name) are not supported in this version.");
    }
}
