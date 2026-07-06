using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dorc.Core.VariableResolution
{
    /// <summary>
    /// Fail-closed allow-list validator for <c>fn:</c> property expressions.
    ///
    /// DOrc <c>fn:</c> expressions are simple string and arithmetic operations
    /// (e.g. <c>"abc".ToUpper()</c>, <c>2 + 3 * 4</c>). They were previously handed
    /// straight to Roslyn's <c>CSharpScript</c>, which can execute ARBITRARY C#
    /// (<c>System.IO.File</c>, <c>System.Diagnostics.Process</c>, reflection) — a
    /// remote-code-execution vector for anyone able to author a property value.
    ///
    /// This validator parses the expression and walks the syntax tree, permitting
    /// ONLY: literals, parentheses, arithmetic/comparison/logical operators, the
    /// ternary conditional, and method/property access whose <b>member name is on an
    /// allow-list</b> — either an allow-listed instance member on a value (string /
    /// number), or any static member of the pure BCL types <c>Math</c>/<c>Convert</c>.
    /// Everything else is refused: bare identifiers (how a type name such as
    /// <c>File</c> is referenced), <c>typeof</c>, <c>new</c>, lambdas, indexers,
    /// generic member names, and any member not on the allow-list.
    ///
    /// Two rules make the reflection escape impossible:
    ///  1. Member names are compared via <see cref="SyntaxToken.ValueText"/> (the
    ///     compiler-bound name), so a Unicode-escaped spelling such as
    ///     <c>GetType</c> cannot slip past the check.
    ///  2. Member names are ALLOW-listed, not deny-listed — reflection entry points
    ///     (<c>GetType</c>, <c>Assembly</c>, <c>GetMethod</c>, <c>MakeGenericType</c>,
    ///     <c>DynamicInvoke</c>, …) are simply absent from the allow-list, so once
    ///     you have any value you still cannot reach a <c>Type</c> or <c>Assembly</c>.
    /// </summary>
    public static class SafeExpressionValidator
    {
        // Bare identifiers permitted as a receiver: pure BCL static types whose
        // members cannot perform IO / process / reflection and do not allocate on an
        // unbounded caller-controlled size.
        private static readonly HashSet<string> AllowedStaticTypes = new(StringComparer.Ordinal)
        {
            "Math",
            "Convert"
        };

        // Instance members permitted on a value receiver (a string/number literal or
        // the result of a safe operation). Deliberately EXCLUDES GetType (the
        // reflection gateway present on every object) and the size-amplifying
        // PadLeft/PadRight (which allow a multi-GB allocation from a single call).
        private static readonly HashSet<string> AllowedInstanceMembers = new(StringComparer.Ordinal)
        {
            "Length",
            "ToString",
            "ToUpper", "ToUpperInvariant",
            "ToLower", "ToLowerInvariant",
            "Trim", "TrimStart", "TrimEnd",
            "Substring",
            "Replace",
            "Contains", "StartsWith", "EndsWith",
            "IndexOf", "LastIndexOf",
            "Insert", "Remove",
            "CompareTo", "Equals"
        };

        public static bool IsSafe(string expression, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(expression))
            {
                reason = "Expression is empty.";
                return false;
            }

            var parsed = SyntaxFactory.ParseExpression(expression);

            if (parsed.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                reason = "Expression is not valid.";
                return false;
            }

            // ParseExpression stops at the first complete expression and ignores any
            // trailing text. Reject input where the parsed expression does not cover
            // the whole string (e.g. a smuggled second statement:
            // `1+1; System.IO.File.ReadAllText(...)`).
            if (parsed.ToString().Trim() != expression.Trim())
            {
                reason = "Expression contains trailing or unparsed content.";
                return false;
            }

            return IsSafeExpression(parsed, out reason);
        }

        private static bool IsSafeExpression(ExpressionSyntax node, out string reason)
        {
            reason = string.Empty;
            switch (node)
            {
                case LiteralExpressionSyntax:
                    return true;

                case ParenthesizedExpressionSyntax paren:
                    return IsSafeExpression(paren.Expression, out reason);

                case PrefixUnaryExpressionSyntax unary
                    when unary.IsKind(SyntaxKind.UnaryMinusExpression)
                      || unary.IsKind(SyntaxKind.UnaryPlusExpression)
                      || unary.IsKind(SyntaxKind.LogicalNotExpression):
                    return IsSafeExpression(unary.Operand, out reason);

                case BinaryExpressionSyntax binary when IsAllowedBinary(binary):
                    return IsSafeExpression(binary.Left, out reason)
                        && IsSafeExpression(binary.Right, out reason);

                case ConditionalExpressionSyntax ternary:
                    return IsSafeExpression(ternary.Condition, out reason)
                        && IsSafeExpression(ternary.WhenTrue, out reason)
                        && IsSafeExpression(ternary.WhenFalse, out reason);

                case MemberAccessExpressionSyntax member
                    when member.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    return IsSafeMemberAccess(member, out reason);

                case InvocationExpressionSyntax invocation:
                    return IsSafeInvocation(invocation, out reason);

                default:
                    reason = $"Disallowed expression element: {node.Kind()}.";
                    return false;
            }
        }

        private static bool IsAllowedBinary(BinaryExpressionSyntax binary)
        {
            switch (binary.Kind())
            {
                case SyntaxKind.AddExpression:
                case SyntaxKind.SubtractExpression:
                case SyntaxKind.MultiplyExpression:
                case SyntaxKind.DivideExpression:
                case SyntaxKind.ModuloExpression:
                case SyntaxKind.EqualsExpression:
                case SyntaxKind.NotEqualsExpression:
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.LessThanOrEqualExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                case SyntaxKind.LogicalAndExpression:
                case SyntaxKind.LogicalOrExpression:
                    return true;
                default:
                    return false;
            }
        }

        private enum ReceiverKind { Rejected, Value, StaticAllowListed }

        /// <summary>
        /// A member name is only usable if it is a plain (non-generic) identifier.
        /// Generic member names (e.g. <c>CreateDelegate&lt;T&gt;</c>) are rejected —
        /// their type-argument list is an attack surface and no simple string/math
        /// operation needs them.
        /// </summary>
        private static bool TryGetMemberName(SimpleNameSyntax name, out string valueText)
        {
            if (name is GenericNameSyntax)
            {
                valueText = string.Empty;
                return false;
            }

            // ValueText, not Text: decodes Unicode escapes so `GetType` is seen
            // as `GetType`, matching how the compiler binds the member.
            valueText = name.Identifier.ValueText;
            return true;
        }

        private static bool IsSafeMemberAccess(MemberAccessExpressionSyntax member, out string reason)
        {
            if (!TryGetMemberName(member.Name, out var memberName))
            {
                reason = "Generic member access is not permitted.";
                return false;
            }

            var receiver = ClassifyReceiver(member.Expression, out reason);
            if (receiver == ReceiverKind.Rejected)
            {
                return false;
            }

            if (receiver == ReceiverKind.Value && !AllowedInstanceMembers.Contains(memberName))
            {
                reason = $"Member '{memberName}' is not permitted.";
                return false;
            }

            return true;
        }

        private static bool IsSafeInvocation(InvocationExpressionSyntax invocation, out string reason)
        {
            // Only method calls of the form <receiver>.<method>(args) are allowed —
            // never a bare call to an in-scope function.
            if (invocation.Expression is not MemberAccessExpressionSyntax member ||
                !member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                reason = "Only method calls on a value or on Math/Convert are permitted.";
                return false;
            }

            if (!TryGetMemberName(member.Name, out var methodName))
            {
                reason = "Generic method calls are not permitted.";
                return false;
            }

            var receiver = ClassifyReceiver(member.Expression, out reason);
            if (receiver == ReceiverKind.Rejected)
            {
                return false;
            }

            // On a value receiver only allow-listed instance methods are callable.
            // On the Math/Convert static types any (non-generic) member is safe.
            if (receiver == ReceiverKind.Value && !AllowedInstanceMembers.Contains(methodName))
            {
                reason = $"Method '{methodName}' is not permitted.";
                return false;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (!IsSafeExpression(argument.Expression, out reason))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Classifies a receiver expression. An allow-listed static type
        /// (<c>Math</c>/<c>Convert</c>) grants access to any of its (pure) members; a
        /// value receiver (literal or safe sub-expression) grants access only to the
        /// allow-listed instance members. A bare identifier that is not an
        /// allow-listed static type is rejected — that is how a type such as
        /// <c>File</c>, <c>Environment</c>, or <c>Type</c> would be referenced.
        /// </summary>
        private static ReceiverKind ClassifyReceiver(ExpressionSyntax receiver, out string reason)
        {
            reason = string.Empty;
            if (receiver is IdentifierNameSyntax identifier)
            {
                if (AllowedStaticTypes.Contains(identifier.Identifier.ValueText))
                {
                    return ReceiverKind.StaticAllowListed;
                }

                reason = $"Identifier '{identifier.Identifier.ValueText}' is not permitted.";
                return ReceiverKind.Rejected;
            }

            return IsSafeExpression(receiver, out reason)
                ? ReceiverKind.Value
                : ReceiverKind.Rejected;
        }
    }
}
