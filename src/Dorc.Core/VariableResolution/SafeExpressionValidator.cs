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
    /// (<c>System.IO.File.ReadAllText</c>, <c>System.Diagnostics.Process.Start</c>,
    /// reflection, …) — a remote-code-execution vector for anyone able to author a
    /// property value.
    ///
    /// This validator parses the expression and walks the syntax tree, permitting
    /// ONLY: literals, parentheses, arithmetic/comparison/logical operators, and
    /// method/property access on string/number literals (or on the safe static
    /// types <c>Math</c>/<c>Convert</c>). Everything else — bare identifiers (which
    /// is how a type name such as <c>File</c> or <c>Environment</c> appears),
    /// <c>typeof</c>, <c>new</c>, lambdas, indexers, and the reflection entry point
    /// <c>GetType</c> — is rejected. Because arbitrary type resolution is impossible,
    /// the RCE class is eliminated by construction rather than merely mitigated.
    /// </summary>
    public static class SafeExpressionValidator
    {
        // The only bare identifiers allowed as a receiver: safe static types whose
        // members cannot perform IO/process/reflection.
        private static readonly HashSet<string> AllowedStaticTypes = new(StringComparer.Ordinal)
        {
            "Math",
            "Convert"
        };

        // Instance/static members that must never be callable: GetType is the entry
        // point to reflection from any object.
        private static readonly HashSet<string> DeniedMemberNames = new(StringComparer.Ordinal)
        {
            "GetType"
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
            // the whole string (e.g. a statement separator used to smuggle a second
            // expression: `1+1; System.IO.File.ReadAllText(...)`).
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

        private static bool IsSafeMemberAccess(MemberAccessExpressionSyntax member, out string reason)
        {
            reason = string.Empty;
            var memberName = member.Name.Identifier.Text;
            if (DeniedMemberNames.Contains(memberName))
            {
                reason = $"Member '{memberName}' is not permitted.";
                return false;
            }

            return IsSafeReceiver(member.Expression, out reason);
        }

        private static bool IsSafeInvocation(InvocationExpressionSyntax invocation, out string reason)
        {
            reason = string.Empty;

            // Only method calls of the form <receiver>.<method>(args) are allowed —
            // never a bare call to an in-scope function.
            if (invocation.Expression is not MemberAccessExpressionSyntax member ||
                !member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                reason = "Only method calls on a value or on Math/Convert are permitted.";
                return false;
            }

            var methodName = member.Name.Identifier.Text;
            if (DeniedMemberNames.Contains(methodName))
            {
                reason = $"Method '{methodName}' is not permitted.";
                return false;
            }

            if (!IsSafeReceiver(member.Expression, out reason))
            {
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
        /// A receiver is safe when it is one of the allow-listed static types
        /// (Math/Convert) or any other safe expression (a literal, or the result of
        /// a safe operation/call). A bare identifier that is not an allow-listed
        /// static type is rejected — that is how a type such as File, Environment,
        /// or Directory would be referenced.
        /// </summary>
        private static bool IsSafeReceiver(ExpressionSyntax receiver, out string reason)
        {
            reason = string.Empty;
            if (receiver is IdentifierNameSyntax identifier)
            {
                if (AllowedStaticTypes.Contains(identifier.Identifier.Text))
                {
                    return true;
                }

                reason = $"Identifier '{identifier.Identifier.Text}' is not permitted.";
                return false;
            }

            return IsSafeExpression(receiver, out reason);
        }
    }
}
