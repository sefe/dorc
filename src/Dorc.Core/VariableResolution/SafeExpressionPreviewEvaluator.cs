using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dorc.Core.VariableResolution
{
    /// <summary>
    /// Evaluates pure, deterministic <c>fn:</c> C# expressions using a read-only syntax-tree
    /// interpreter without compiling IL or executing arbitrary code. Any expression that uses
    /// non-deterministic APIs (clocks, randoms, GUIDs), I/O, reflection, environment state,
    /// unknown types/methods, or invalid arguments returns <c>false</c> so the caller can
    /// safely fall back to <c>ExpressionDeferred</c>.
    /// </summary>
    public sealed class SafeExpressionPreviewEvaluator
    {
        private const string ExpressionMarker = "fn:";
        private const int MaxExpressionLength = 4_096;
        private const int MaxAstDepth = 32;
        private const int MaxEvaluationSteps = 256;
        private const int MaxEvaluatedStringLength = 65_536;
        private const int MaxMethodArguments = 16;

        private enum AllowedStaticReceiver
        {
            None,
            String,
            Math,
            Int32,
            Int64,
            Double,
            Decimal,
            Boolean,
            Convert,
            StringComparison,
            StringSplitOptions
        }

        public bool TryEvaluate(string? candidate, out object? evaluatedValue)
        {
            evaluatedValue = null;

            if (string.IsNullOrEmpty(candidate) ||
                !candidate.StartsWith(ExpressionMarker, StringComparison.Ordinal))
            {
                return false;
            }

            var expressionText = candidate.Substring(ExpressionMarker.Length).Trim();
            if (string.IsNullOrEmpty(expressionText) || expressionText.Length > MaxExpressionLength)
            {
                return false;
            }

            var parsed = SyntaxFactory.ParseExpression(expressionText);
            if (parsed.ContainsDiagnostics || parsed.FullSpan.Length != expressionText.Length)
            {
                return false;
            }

            if (parsed.SyntaxTree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return false;
            }

            var remainingSteps = MaxEvaluationSteps;
            if (!TryEvaluateNode(parsed, 0, ref remainingSteps, out var rawResult) || rawResult == null)
            {
                return false;
            }

            if (rawResult is char ch)
            {
                evaluatedValue = ch.ToString();
                return true;
            }

            if (rawResult is string str && str.Length > MaxEvaluatedStringLength)
            {
                return false;
            }

            if (rawResult is double d && (double.IsNaN(d) || double.IsInfinity(d)))
            {
                return false;
            }

            evaluatedValue = rawResult;
            return true;
        }

        private static bool TryEvaluateNode(
            ExpressionSyntax node,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;

            if (depth > MaxAstDepth || --remainingSteps < 0)
            {
                return false;
            }

            return node switch
            {
                ParenthesizedExpressionSyntax parenthesized =>
                    TryEvaluateNode(parenthesized.Expression, depth + 1, ref remainingSteps, out result),
                LiteralExpressionSyntax literal =>
                    TryEvaluateLiteral(literal, out result),
                InterpolatedStringExpressionSyntax interpolated =>
                    TryEvaluateInterpolatedString(interpolated, depth + 1, ref remainingSteps, out result),
                PrefixUnaryExpressionSyntax prefixUnary =>
                    TryEvaluatePrefixUnary(prefixUnary, depth + 1, ref remainingSteps, out result),
                BinaryExpressionSyntax binary =>
                    TryEvaluateBinary(binary, depth + 1, ref remainingSteps, out result),
                ConditionalExpressionSyntax conditional =>
                    TryEvaluateConditional(conditional, depth + 1, ref remainingSteps, out result),
                ElementAccessExpressionSyntax elementAccess =>
                    TryEvaluateElementAccess(elementAccess, depth + 1, ref remainingSteps, out result),
                MemberAccessExpressionSyntax memberAccess =>
                    TryEvaluateMemberAccess(memberAccess, depth + 1, ref remainingSteps, out result),
                InvocationExpressionSyntax invocation =>
                    TryEvaluateInvocation(invocation, depth + 1, ref remainingSteps, out result),
                _ => false
            };
        }

        private static bool TryEvaluateLiteral(LiteralExpressionSyntax literal, out object? result)
        {
            result = null;
            switch (literal.Kind())
            {
                case SyntaxKind.StringLiteralExpression:
                    if (literal.Token.ValueText.Length > MaxEvaluatedStringLength)
                    {
                        return false;
                    }

                    result = literal.Token.ValueText;
                    return true;

                case SyntaxKind.CharacterLiteralExpression:
                    if (literal.Token.Value is char c)
                    {
                        result = c;
                        return true;
                    }

                    return false;

                case SyntaxKind.NumericLiteralExpression:
                    switch (literal.Token.Value)
                    {
                        case int i:
                            result = i;
                            return true;
                        case long l:
                            result = l;
                            return true;
                        case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                            result = d;
                            return true;
                        case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                            result = (double)f;
                            return true;
                        case decimal m:
                            result = m;
                            return true;
                        default:
                            return false;
                    }

                case SyntaxKind.TrueLiteralExpression:
                    result = true;
                    return true;

                case SyntaxKind.FalseLiteralExpression:
                    result = false;
                    return true;

                case SyntaxKind.NullLiteralExpression:
                    result = null;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateInterpolatedString(
            InterpolatedStringExpressionSyntax interpolated,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            var sb = new StringBuilder();

            foreach (var content in interpolated.Contents)
            {
                switch (content)
                {
                    case InterpolatedStringTextSyntax textPart:
                        var literalText = textPart.TextToken.ValueText;
                        if (sb.Length + literalText.Length > MaxEvaluatedStringLength)
                        {
                            return false;
                        }

                        sb.Append(literalText);
                        break;

                    case InterpolationSyntax interpolation:
                        if (interpolation.AlignmentClause != null || interpolation.FormatClause != null)
                        {
                            return false;
                        }

                        if (!TryEvaluateNode(interpolation.Expression, depth + 1, ref remainingSteps, out var partVal) ||
                            !TryFormatScalar(partVal, out var formattedPart))
                        {
                            return false;
                        }

                        if (sb.Length + formattedPart.Length > MaxEvaluatedStringLength)
                        {
                            return false;
                        }

                        sb.Append(formattedPart);
                        break;

                    default:
                        return false;
                }
            }

            result = sb.ToString();
            return true;
        }

        private static bool TryEvaluatePrefixUnary(
            PrefixUnaryExpressionSyntax prefixUnary,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            if (!TryEvaluateNode(prefixUnary.Operand, depth + 1, ref remainingSteps, out var operand) ||
                operand == null)
            {
                return false;
            }

            try
            {
                switch (prefixUnary.Kind())
                {
                    case SyntaxKind.LogicalNotExpression when operand is bool b:
                        result = !b;
                        return true;

                    case SyntaxKind.UnaryPlusExpression when operand is int or long or double or decimal:
                        result = operand;
                        return true;

                    case SyntaxKind.UnaryMinusExpression:
                        switch (operand)
                        {
                            case int i:
                                result = checked(-i);
                                return true;
                            case long l:
                                result = checked(-l);
                                return true;
                            case double d:
                                result = -d;
                                return true;
                            case decimal m:
                                result = -m;
                                return true;
                            default:
                                return false;
                        }

                    case SyntaxKind.BitwiseNotExpression:
                        switch (operand)
                        {
                            case int i:
                                result = ~i;
                                return true;
                            case long l:
                                result = ~l;
                                return true;
                            default:
                                return false;
                        }

                    default:
                        return false;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryEvaluateBinary(
            BinaryExpressionSyntax binary,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            var kind = binary.Kind();

            if (kind == SyntaxKind.LogicalAndExpression)
            {
                if (!TryEvaluateNode(binary.Left, depth + 1, ref remainingSteps, out var leftObj) ||
                    leftObj is not bool leftBool)
                {
                    return false;
                }

                if (!leftBool)
                {
                    result = false;
                    return true;
                }

                if (!TryEvaluateNode(binary.Right, depth + 1, ref remainingSteps, out var rightObj) ||
                    rightObj is not bool rightBool)
                {
                    return false;
                }

                result = rightBool;
                return true;
            }

            if (kind == SyntaxKind.LogicalOrExpression)
            {
                if (!TryEvaluateNode(binary.Left, depth + 1, ref remainingSteps, out var leftObj) ||
                    leftObj is not bool leftBool)
                {
                    return false;
                }

                if (leftBool)
                {
                    result = true;
                    return true;
                }

                if (!TryEvaluateNode(binary.Right, depth + 1, ref remainingSteps, out var rightObj) ||
                    rightObj is not bool rightBool)
                {
                    return false;
                }

                result = rightBool;
                return true;
            }

            if (kind == SyntaxKind.CoalesceExpression)
            {
                if (!TryEvaluateNode(binary.Left, depth + 1, ref remainingSteps, out var leftObj))
                {
                    return false;
                }

                if (leftObj != null)
                {
                    result = leftObj;
                    return true;
                }

                return TryEvaluateNode(binary.Right, depth + 1, ref remainingSteps, out result);
            }

            if (!TryEvaluateNode(binary.Left, depth + 1, ref remainingSteps, out var left) ||
                !TryEvaluateNode(binary.Right, depth + 1, ref remainingSteps, out var right))
            {
                return false;
            }

            if (kind == SyntaxKind.AddExpression && (left is string || right is string))
            {
                if (!TryFormatScalar(left, out var leftStr) || !TryFormatScalar(right, out var rightStr))
                {
                    return false;
                }

                if (leftStr.Length + rightStr.Length > MaxEvaluatedStringLength)
                {
                    return false;
                }

                result = leftStr + rightStr;
                return true;
            }

            return kind switch
            {
                SyntaxKind.AddExpression or
                SyntaxKind.SubtractExpression or
                SyntaxKind.MultiplyExpression or
                SyntaxKind.DivideExpression or
                SyntaxKind.ModuloExpression =>
                    TryEvaluateArithmetic(kind, left, right, out result),
                SyntaxKind.EqualsExpression =>
                    TryEvaluateEquality(left, right, out var eq) && AssignBool(eq, out result),
                SyntaxKind.NotEqualsExpression =>
                    TryEvaluateEquality(left, right, out var neq) && AssignBool(!neq, out result),
                SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression =>
                    TryEvaluateRelational(kind, left, right, out result),
                _ => false
            };
        }

        private static bool AssignBool(bool value, out object? result)
        {
            result = value;
            return true;
        }

        private static bool TryEvaluateArithmetic(
            SyntaxKind kind,
            object? left,
            object? right,
            out object? result)
        {
            result = null;
            if (left == null || right == null)
            {
                return false;
            }

            try
            {
                if (left is decimal || right is decimal)
                {
                    if (left is double || right is double)
                    {
                        return false;
                    }

                    if (!TryToDecimal(left, out var dl) || !TryToDecimal(right, out var dr))
                    {
                        return false;
                    }

                    if (kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression && dr == 0m)
                    {
                        return false;
                    }

                    result = kind switch
                    {
                        SyntaxKind.AddExpression => checked(dl + dr),
                        SyntaxKind.SubtractExpression => checked(dl - dr),
                        SyntaxKind.MultiplyExpression => checked(dl * dr),
                        SyntaxKind.DivideExpression => checked(dl / dr),
                        SyntaxKind.ModuloExpression => checked(dl % dr),
                        _ => null
                    };
                    return result != null;
                }

                if (left is double || right is double)
                {
                    if (!TryToDouble(left, out var dblL) || !TryToDouble(right, out var dblR))
                    {
                        return false;
                    }

                    if (kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression && dblR is 0.0 or -0.0)
                    {
                        return false;
                    }

                    var dblResult = kind switch
                    {
                        SyntaxKind.AddExpression => dblL + dblR,
                        SyntaxKind.SubtractExpression => dblL - dblR,
                        SyntaxKind.MultiplyExpression => dblL * dblR,
                        SyntaxKind.DivideExpression => dblL / dblR,
                        SyntaxKind.ModuloExpression => dblL % dblR,
                        _ => double.NaN
                    };

                    if (double.IsNaN(dblResult) || double.IsInfinity(dblResult))
                    {
                        return false;
                    }

                    result = dblResult;
                    return true;
                }

                if (left is long || right is long)
                {
                    if (!TryToInt64(left, out var ll) || !TryToInt64(right, out var lr))
                    {
                        return false;
                    }

                    if (kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression && lr == 0L)
                    {
                        return false;
                    }

                    result = kind switch
                    {
                        SyntaxKind.AddExpression => checked(ll + lr),
                        SyntaxKind.SubtractExpression => checked(ll - lr),
                        SyntaxKind.MultiplyExpression => checked(ll * lr),
                        SyntaxKind.DivideExpression => checked(ll / lr),
                        SyntaxKind.ModuloExpression => checked(ll % lr),
                        _ => null
                    };
                    return result != null;
                }

                if (TryToInt32(left, out var il) && TryToInt32(right, out var ir))
                {
                    if (kind is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression && ir == 0)
                    {
                        return false;
                    }

                    result = kind switch
                    {
                        SyntaxKind.AddExpression => checked(il + ir),
                        SyntaxKind.SubtractExpression => checked(il - ir),
                        SyntaxKind.MultiplyExpression => checked(il * ir),
                        SyntaxKind.DivideExpression => checked(il / ir),
                        SyntaxKind.ModuloExpression => checked(il % ir),
                        _ => null
                    };
                    return result != null;
                }

                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryEvaluateEquality(object? left, object? right, out bool isEqual)
        {
            isEqual = false;
            if (left == null && right == null)
            {
                isEqual = true;
                return true;
            }

            if (left == null || right == null)
            {
                isEqual = false;
                return true;
            }

            if (left is string ls && right is string rs)
            {
                isEqual = string.Equals(ls, rs, StringComparison.Ordinal);
                return true;
            }

            if (left is bool lb && right is bool rb)
            {
                isEqual = lb == rb;
                return true;
            }

            if (left is char lc && right is char rc)
            {
                isEqual = lc == rc;
                return true;
            }

            if (TryCompareNumeric(left, right, out var cmp))
            {
                isEqual = cmp == 0;
                return true;
            }

            return false;
        }

        private static bool TryEvaluateRelational(
            SyntaxKind kind,
            object? left,
            object? right,
            out object? result)
        {
            result = null;
            int cmp;
            if (left is char lc && right is char rc)
            {
                cmp = lc.CompareTo(rc);
            }
            else if (!TryCompareNumeric(left, right, out cmp))
            {
                return false;
            }

            result = kind switch
            {
                SyntaxKind.LessThanExpression => cmp < 0,
                SyntaxKind.LessThanOrEqualExpression => cmp <= 0,
                SyntaxKind.GreaterThanExpression => cmp > 0,
                SyntaxKind.GreaterThanOrEqualExpression => cmp >= 0,
                _ => (object?)null
            };
            return result != null;
        }

        private static bool TryCompareNumeric(object? left, object? right, out int comparison)
        {
            comparison = 0;
            if (left == null || right == null)
            {
                return false;
            }

            if (left is decimal || right is decimal)
            {
                if (left is double || right is double ||
                    !TryToDecimal(left, out var dl) ||
                    !TryToDecimal(right, out var dr))
                {
                    return false;
                }

                comparison = dl.CompareTo(dr);
                return true;
            }

            if (left is double || right is double)
            {
                if (!TryToDouble(left, out var dblL) || !TryToDouble(right, out var dblR))
                {
                    return false;
                }

                comparison = dblL.CompareTo(dblR);
                return true;
            }

            if (TryToInt64(left, out var ll) && TryToInt64(right, out var lr))
            {
                comparison = ll.CompareTo(lr);
                return true;
            }

            return false;
        }

        private static bool TryEvaluateConditional(
            ConditionalExpressionSyntax conditional,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            if (!TryEvaluateNode(conditional.Condition, depth + 1, ref remainingSteps, out var condObj) ||
                condObj is not bool condBool)
            {
                return false;
            }

            var branch = condBool ? conditional.WhenTrue : conditional.WhenFalse;
            return TryEvaluateNode(branch, depth + 1, ref remainingSteps, out result);
        }

        private static bool TryEvaluateElementAccess(
            ElementAccessExpressionSyntax elementAccess,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var argSyntax = elementAccess.ArgumentList.Arguments[0];
            if (argSyntax.NameColon != null || !argSyntax.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return false;
            }

            if (!TryEvaluateNode(elementAccess.Expression, depth + 1, ref remainingSteps, out var target) ||
                !TryEvaluateNode(argSyntax.Expression, depth + 1, ref remainingSteps, out var indexObj) ||
                indexObj is not int index)
            {
                return false;
            }

            switch (target)
            {
                case string s when index >= 0 && index < s.Length:
                    result = s[index];
                    return true;

                case string[] arr when index >= 0 && index < arr.Length:
                    result = arr[index];
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            var memberName = memberAccess.Name.Identifier.ValueText;

            if (TryGetStaticReceiver(memberAccess.Expression, out var staticReceiver))
            {
                switch (staticReceiver)
                {
                    case AllowedStaticReceiver.String when memberName == "Empty":
                        result = string.Empty;
                        return true;

                    case AllowedStaticReceiver.StringComparison
                        when Enum.TryParse<StringComparison>(memberName, false, out var comparison):
                        result = comparison;
                        return true;

                    case AllowedStaticReceiver.StringSplitOptions
                        when Enum.TryParse<StringSplitOptions>(memberName, false, out var splitOptions):
                        result = splitOptions;
                        return true;

                    default:
                        return false;
                }
            }

            if (!TryEvaluateNode(memberAccess.Expression, depth + 1, ref remainingSteps, out var target) ||
                target == null)
            {
                return false;
            }

            switch (target)
            {
                case string s when memberName == "Length":
                    result = s.Length;
                    return true;

                case string[] arr when memberName == "Length":
                    result = arr.Length;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateInvocation(
            InvocationExpressionSyntax invocation,
            int depth,
            ref int remainingSteps,
            out object? result)
        {
            result = null;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var rawArgs = invocation.ArgumentList.Arguments;
            if (rawArgs.Count > MaxMethodArguments)
            {
                return false;
            }

            var evaluatedArgs = new object?[rawArgs.Count];
            for (var i = 0; i < rawArgs.Count; i++)
            {
                var argSyntax = rawArgs[i];
                if (argSyntax.NameColon != null || !argSyntax.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    return false;
                }

                if (!TryEvaluateNode(argSyntax.Expression, depth + 1, ref remainingSteps, out evaluatedArgs[i]))
                {
                    return false;
                }
            }

            var methodName = memberAccess.Name.Identifier.ValueText;

            if (TryGetStaticReceiver(memberAccess.Expression, out var staticReceiver))
            {
                return TryEvaluateStaticMethod(staticReceiver, methodName, evaluatedArgs, out result);
            }

            if (!TryEvaluateNode(memberAccess.Expression, depth + 1, ref remainingSteps, out var target) ||
                target == null)
            {
                return false;
            }

            if (target is string targetString)
            {
                return TryEvaluateStringInstanceMethod(targetString, methodName, evaluatedArgs, out result);
            }

            if (methodName == "ToString" &&
                evaluatedArgs.Length == 0 &&
                target is int or long or double or decimal or bool or char &&
                TryFormatScalar(target, out var scalarText))
            {
                result = scalarText;
                return true;
            }

            return false;
        }

        private static bool TryEvaluateStaticMethod(
            AllowedStaticReceiver receiver,
            string methodName,
            object?[] args,
            out object? result)
        {
            result = null;

            switch (receiver)
            {
                case AllowedStaticReceiver.String:
                    return TryEvaluateStringStaticMethod(methodName, args, out result);

                case AllowedStaticReceiver.Math:
                    return TryEvaluateMathStaticMethod(methodName, args, out result);

                case AllowedStaticReceiver.Int32 when methodName == "Parse" && args is [string intText]:
                    if (int.TryParse(intText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                    {
                        result = parsedInt;
                        return true;
                    }

                    return false;

                case AllowedStaticReceiver.Int64 when methodName == "Parse" && args is [string longText]:
                    if (long.TryParse(longText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
                    {
                        result = parsedLong;
                        return true;
                    }

                    return false;

                case AllowedStaticReceiver.Double when methodName == "Parse" && args is [string dblText]:
                    if (double.TryParse(
                            dblText,
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out var parsedDbl) &&
                        !double.IsNaN(parsedDbl) &&
                        !double.IsInfinity(parsedDbl))
                    {
                        result = parsedDbl;
                        return true;
                    }

                    return false;

                case AllowedStaticReceiver.Decimal when methodName == "Parse" && args is [string decText]:
                    if (decimal.TryParse(decText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDec))
                    {
                        result = parsedDec;
                        return true;
                    }

                    return false;

                case AllowedStaticReceiver.Boolean when methodName == "Parse" && args is [string boolText]:
                    if (bool.TryParse(boolText, out var parsedBool))
                    {
                        result = parsedBool;
                        return true;
                    }

                    return false;

                case AllowedStaticReceiver.Convert:
                    return TryEvaluateConvertStaticMethod(methodName, args, out result);

                default:
                    return false;
            }
        }

        private static bool TryEvaluateStringStaticMethod(
            string methodName,
            object?[] args,
            out object? result)
        {
            result = null;

            switch (methodName)
            {
                case "IsNullOrEmpty" when args.Length == 1 && (args[0] is null or string):
                    result = string.IsNullOrEmpty((string?)args[0]);
                    return true;

                case "IsNullOrWhiteSpace" when args.Length == 1 && (args[0] is null or string):
                    result = string.IsNullOrWhiteSpace((string?)args[0]);
                    return true;

                case "Concat" when args.Length >= 1:
                {
                    var sb = new StringBuilder();
                    foreach (var arg in args)
                    {
                        if (!TryFormatScalar(arg, out var text))
                        {
                            return false;
                        }

                        if (sb.Length + text.Length > MaxEvaluatedStringLength)
                        {
                            return false;
                        }

                        sb.Append(text);
                    }

                    result = sb.ToString();
                    return true;
                }

                case "Join" when args.Length >= 2 && TryFormatSeparator(args[0], out var separator):
                {
                    IReadOnlyList<string> items;
                    if (args.Length == 2 && args[1] is string[] arrayItems)
                    {
                        items = arrayItems;
                    }
                    else
                    {
                        var scalarItems = new List<string>(args.Length - 1);
                        for (var i = 1; i < args.Length; i++)
                        {
                            if (!TryFormatScalar(args[i], out var itemText))
                            {
                                return false;
                            }

                            scalarItems.Add(itemText);
                        }

                        items = scalarItems;
                    }

                    var sb = new StringBuilder();
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (i > 0)
                        {
                            if (sb.Length + separator.Length > MaxEvaluatedStringLength)
                            {
                                return false;
                            }

                            sb.Append(separator);
                        }

                        var current = items[i] ?? string.Empty;
                        if (sb.Length + current.Length > MaxEvaluatedStringLength)
                        {
                            return false;
                        }

                        sb.Append(current);
                    }

                    result = sb.ToString();
                    return true;
                }

                case "Equals" when args is [string or null, string or null]:
                    result = string.Equals((string?)args[0], (string?)args[1], StringComparison.Ordinal);
                    return true;

                case "Equals" when args is [string or null, string or null, StringComparison cmp]:
                    result = string.Equals((string?)args[0], (string?)args[1], cmp);
                    return true;

                case "Compare" when args is [string or null, string or null]:
                    result = string.Compare((string?)args[0], (string?)args[1], StringComparison.Ordinal);
                    return true;

                case "Compare" when args is [string or null, string or null, StringComparison cmp]:
                    result = string.Compare((string?)args[0], (string?)args[1], cmp);
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateMathStaticMethod(
            string methodName,
            object?[] args,
            out object? result)
        {
            result = null;

            switch (methodName)
            {
                case "Abs" when args.Length == 1:
                    switch (args[0])
                    {
                        case int i when i != int.MinValue:
                            result = Math.Abs(i);
                            return true;
                        case long l when l != long.MinValue:
                            result = Math.Abs(l);
                            return true;
                        case double d:
                            result = Math.Abs(d);
                            return true;
                        case decimal m:
                            result = Math.Abs(m);
                            return true;
                        default:
                            return false;
                    }

                case "Min" when args.Length == 2 && TryCompareNumeric(args[0], args[1], out var minCmp):
                    result = minCmp <= 0 ? PromoteNumericPair(args[0]!, args[1]!, true) : PromoteNumericPair(args[0]!, args[1]!, false);
                    return result != null;

                case "Max" when args.Length == 2 && TryCompareNumeric(args[0], args[1], out var maxCmp):
                    result = maxCmp >= 0 ? PromoteNumericPair(args[0]!, args[1]!, true) : PromoteNumericPair(args[0]!, args[1]!, false);
                    return result != null;

                case "Clamp" when args.Length == 3 &&
                                  TryCompareNumeric(args[1], args[2], out var boundsCmp) &&
                                  boundsCmp <= 0 &&
                                  TryCompareNumeric(args[0], args[1], out var lowCmp) &&
                                  TryCompareNumeric(args[0], args[2], out var highCmp):
                    result = lowCmp < 0 ? args[1] : highCmp > 0 ? args[2] : args[0];
                    return true;

                case "Floor" when args.Length == 1:
                    switch (args[0])
                    {
                        case double d:
                            result = Math.Floor(d);
                            return true;
                        case decimal m:
                            result = Math.Floor(m);
                            return true;
                        default:
                            return false;
                    }

                case "Ceiling" when args.Length == 1:
                    switch (args[0])
                    {
                        case double d:
                            result = Math.Ceiling(d);
                            return true;
                        case decimal m:
                            result = Math.Ceiling(m);
                            return true;
                        default:
                            return false;
                    }

                case "Round" when args.Length == 1:
                    switch (args[0])
                    {
                        case double d:
                            result = Math.Round(d);
                            return true;
                        case decimal m:
                            result = Math.Round(m);
                            return true;
                        default:
                            return false;
                    }

                case "Round" when args is [double dVal, int digits] && digits is >= 0 and <= 15:
                    result = Math.Round(dVal, digits);
                    return true;

                case "Round" when args is [decimal mVal, int mDigits] && mDigits is >= 0 and <= 28:
                    result = Math.Round(mVal, mDigits);
                    return true;

                default:
                    return false;
            }
        }

        private static object? PromoteNumericPair(object left, object right, bool pickLeft)
        {
            if (left is decimal || right is decimal)
            {
                return TryToDecimal(pickLeft ? left : right, out var dec) ? dec : null;
            }

            if (left is double || right is double)
            {
                return TryToDouble(pickLeft ? left : right, out var dbl) ? dbl : null;
            }

            if (left is long || right is long)
            {
                return TryToInt64(pickLeft ? left : right, out var lng) ? lng : null;
            }

            return TryToInt32(pickLeft ? left : right, out var i) ? i : null;
        }

        private static bool TryEvaluateConvertStaticMethod(
            string methodName,
            object?[] args,
            out object? result)
        {
            result = null;
            if (args.Length != 1)
            {
                return false;
            }

            var arg = args[0];
            switch (methodName)
            {
                case "ToInt32":
                    if (arg is string sInt && int.TryParse(sInt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                    {
                        result = parsedInt;
                        return true;
                    }

                    if (arg is bool bInt)
                    {
                        result = bInt ? 1 : 0;
                        return true;
                    }

                    if (TryToInt32(arg, out var convInt))
                    {
                        result = convInt;
                        return true;
                    }

                    return false;

                case "ToInt64":
                    if (arg is string sLong && long.TryParse(sLong, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
                    {
                        result = parsedLong;
                        return true;
                    }

                    if (TryToInt64(arg, out var convLong))
                    {
                        result = convLong;
                        return true;
                    }

                    return false;

                case "ToBoolean":
                    if (arg is bool bVal)
                    {
                        result = bVal;
                        return true;
                    }

                    if (arg is string sBool && bool.TryParse(sBool, out var parsedBool))
                    {
                        result = parsedBool;
                        return true;
                    }

                    if (arg is int iBool)
                    {
                        result = iBool != 0;
                        return true;
                    }

                    return false;

                case "ToString":
                    if (TryFormatScalar(arg, out var formatted))
                    {
                        result = formatted;
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateStringInstanceMethod(
            string target,
            string methodName,
            object?[] args,
            out object? result)
        {
            result = null;

            switch (methodName)
            {
                case "ToLower" or "ToLowerInvariant" when args.Length == 0:
                    result = target.ToLowerInvariant();
                    return true;

                case "ToUpper" or "ToUpperInvariant" when args.Length == 0:
                    result = target.ToUpperInvariant();
                    return true;

                case "ToString" when args.Length == 0:
                    result = target;
                    return true;

                case "Trim" when args.Length == 0:
                    result = target.Trim();
                    return true;

                case "Trim" when args.Length == 1 && TryExtractTrimChars(args[0], out var trimChars):
                    result = target.Trim(trimChars);
                    return true;

                case "TrimStart" when args.Length == 0:
                    result = target.TrimStart();
                    return true;

                case "TrimStart" when args.Length == 1 && TryExtractTrimChars(args[0], out var trimStartChars):
                    result = target.TrimStart(trimStartChars);
                    return true;

                case "TrimEnd" when args.Length == 0:
                    result = target.TrimEnd();
                    return true;

                case "TrimEnd" when args.Length == 1 && TryExtractTrimChars(args[0], out var trimEndChars):
                    result = target.TrimEnd(trimEndChars);
                    return true;

                case "Replace" when args is [string oldValue, string newValue]:
                    if (string.IsNullOrEmpty(oldValue))
                    {
                        return false;
                    }

                    if (newValue.Length > oldValue.Length)
                    {
                        var occurrences = CountOccurrences(target, oldValue);
                        var projectedLength = (long)target.Length + (long)occurrences * (newValue.Length - oldValue.Length);
                        if (projectedLength > MaxEvaluatedStringLength)
                        {
                            return false;
                        }
                    }

                    result = target.Replace(oldValue, newValue, StringComparison.Ordinal);
                    return true;

                case "Replace" when args is [char oldChar, char newChar]:
                    result = target.Replace(oldChar, newChar);
                    return true;

                case "Substring" when args is [int startIndex]:
                    if (startIndex < 0 || startIndex > target.Length)
                    {
                        return false;
                    }

                    result = target.Substring(startIndex);
                    return true;

                case "Substring" when args is [int startIdx, int length]:
                    if (startIdx < 0 || length < 0 || (long)startIdx + length > target.Length)
                    {
                        return false;
                    }

                    result = target.Substring(startIdx, length);
                    return true;

                case "StartsWith" when args is [string prefix]:
                    result = target.StartsWith(prefix, StringComparison.Ordinal);
                    return true;

                case "StartsWith" when args is [string prefix, StringComparison startsCmp]:
                    result = target.StartsWith(prefix, startsCmp);
                    return true;

                case "StartsWith" when args is [char prefixChar]:
                    result = target.StartsWith(prefixChar);
                    return true;

                case "EndsWith" when args is [string suffix]:
                    result = target.EndsWith(suffix, StringComparison.Ordinal);
                    return true;

                case "EndsWith" when args is [string suffix, StringComparison endsCmp]:
                    result = target.EndsWith(suffix, endsCmp);
                    return true;

                case "EndsWith" when args is [char suffixChar]:
                    result = target.EndsWith(suffixChar);
                    return true;

                case "Contains" when args is [string substring]:
                    result = target.Contains(substring, StringComparison.Ordinal);
                    return true;

                case "Contains" when args is [string substring, StringComparison containsCmp]:
                    result = target.Contains(substring, containsCmp);
                    return true;

                case "Contains" when args is [char ch]:
                    result = target.Contains(ch);
                    return true;

                case "IndexOf" when args is [string searchStr]:
                    result = target.IndexOf(searchStr, StringComparison.Ordinal);
                    return true;

                case "IndexOf" when args is [string searchStr, StringComparison indexCmp]:
                    result = target.IndexOf(searchStr, indexCmp);
                    return true;

                case "IndexOf" when args is [string searchStr, int searchStart] &&
                                      searchStart >= 0 &&
                                      searchStart <= target.Length:
                    result = target.IndexOf(searchStr, searchStart, StringComparison.Ordinal);
                    return true;

                case "IndexOf" when args is [char searchChar]:
                    result = target.IndexOf(searchChar);
                    return true;

                case "LastIndexOf" when args is [string lastStr]:
                    result = target.LastIndexOf(lastStr, StringComparison.Ordinal);
                    return true;

                case "LastIndexOf" when args is [char lastChar]:
                    result = target.LastIndexOf(lastChar);
                    return true;

                case "PadLeft" when args is [int totalWidth] &&
                                    totalWidth >= 0 &&
                                    totalWidth <= MaxEvaluatedStringLength:
                    result = target.PadLeft(totalWidth);
                    return true;

                case "PadLeft" when args is [int totalWidth, char padChar] &&
                                    totalWidth >= 0 &&
                                    totalWidth <= MaxEvaluatedStringLength:
                    result = target.PadLeft(totalWidth, padChar);
                    return true;

                case "PadRight" when args is [int totalWidth] &&
                                     totalWidth >= 0 &&
                                     totalWidth <= MaxEvaluatedStringLength:
                    result = target.PadRight(totalWidth);
                    return true;

                case "PadRight" when args is [int totalWidth, char padChar] &&
                                     totalWidth >= 0 &&
                                     totalWidth <= MaxEvaluatedStringLength:
                    result = target.PadRight(totalWidth, padChar);
                    return true;

                case "Remove" when args is [int removeStart] &&
                                   removeStart >= 0 &&
                                   removeStart < target.Length:
                    result = target.Remove(removeStart);
                    return true;

                case "Remove" when args is [int removeStart, int count] &&
                                   removeStart >= 0 &&
                                   count >= 0 &&
                                   (long)removeStart + count <= target.Length:
                    result = target.Remove(removeStart, count);
                    return true;

                case "Insert" when args is [int insertIndex, string insertValue] &&
                                   insertIndex >= 0 &&
                                   insertIndex <= target.Length &&
                                   target.Length + insertValue.Length <= MaxEvaluatedStringLength:
                    result = target.Insert(insertIndex, insertValue);
                    return true;

                case "Split" when args is [char splitChar]:
                    result = target.Split(splitChar);
                    return true;

                case "Split" when args is [string splitStr]:
                    result = target.Split(splitStr);
                    return true;

                case "Split" when args is [char splitChar, StringSplitOptions splitOptions]:
                    result = target.Split(splitChar, splitOptions);
                    return true;

                case "Split" when args is [string splitStr, StringSplitOptions splitOptions]:
                    result = target.Split(splitStr, splitOptions);
                    return true;

                case "Equals" when args is [string other]:
                    result = string.Equals(target, other, StringComparison.Ordinal);
                    return true;

                case "Equals" when args is [string other, StringComparison eqCmp]:
                    result = string.Equals(target, other, eqCmp);
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryExtractTrimChars(object? arg, out char[] chars)
        {
            switch (arg)
            {
                case char c:
                    chars = new[] { c };
                    return true;
                case string s:
                    chars = s.ToCharArray();
                    return true;
                default:
                    chars = Array.Empty<char>();
                    return false;
            }
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += pattern.Length;
            }

            return count;
        }

        private static bool TryGetStaticReceiver(ExpressionSyntax expression, out AllowedStaticReceiver receiver)
        {
            receiver = AllowedStaticReceiver.None;

            if (expression is PredefinedTypeSyntax predefined)
            {
                receiver = predefined.Keyword.Kind() switch
                {
                    SyntaxKind.StringKeyword => AllowedStaticReceiver.String,
                    SyntaxKind.IntKeyword => AllowedStaticReceiver.Int32,
                    SyntaxKind.LongKeyword => AllowedStaticReceiver.Int64,
                    SyntaxKind.DoubleKeyword => AllowedStaticReceiver.Double,
                    SyntaxKind.DecimalKeyword => AllowedStaticReceiver.Decimal,
                    SyntaxKind.BoolKeyword => AllowedStaticReceiver.Boolean,
                    _ => AllowedStaticReceiver.None
                };
                return receiver != AllowedStaticReceiver.None;
            }

            string? typeName = null;
            if (expression is IdentifierNameSyntax identifier)
            {
                typeName = identifier.Identifier.ValueText;
            }
            else if (expression is MemberAccessExpressionSyntax
                     {
                         Expression: IdentifierNameSyntax { Identifier.ValueText: "System" }
                     } systemQualified)
            {
                typeName = systemQualified.Name.Identifier.ValueText;
            }

            if (typeName == null)
            {
                return false;
            }

            receiver = typeName switch
            {
                "String" => AllowedStaticReceiver.String,
                "Math" => AllowedStaticReceiver.Math,
                "Int32" => AllowedStaticReceiver.Int32,
                "Int64" => AllowedStaticReceiver.Int64,
                "Double" => AllowedStaticReceiver.Double,
                "Decimal" => AllowedStaticReceiver.Decimal,
                "Boolean" => AllowedStaticReceiver.Boolean,
                "Convert" => AllowedStaticReceiver.Convert,
                "StringComparison" => AllowedStaticReceiver.StringComparison,
                "StringSplitOptions" => AllowedStaticReceiver.StringSplitOptions,
                _ => AllowedStaticReceiver.None
            };

            return receiver != AllowedStaticReceiver.None;
        }

        private static bool TryFormatSeparator(object? value, out string separator)
        {
            switch (value)
            {
                case null:
                    separator = string.Empty;
                    return true;
                case string s:
                    separator = s;
                    return true;
                case char c:
                    separator = c.ToString();
                    return true;
                default:
                    separator = string.Empty;
                    return false;
            }
        }

        private static bool TryFormatScalar(object? value, out string formatted)
        {
            switch (value)
            {
                case null:
                    formatted = string.Empty;
                    return true;
                case string s:
                    formatted = s;
                    return true;
                case char c:
                    formatted = c.ToString();
                    return true;
                case bool b:
                    formatted = b ? "True" : "False";
                    return true;
                case int i:
                    formatted = i.ToString(CultureInfo.InvariantCulture);
                    return true;
                case long l:
                    formatted = l.ToString(CultureInfo.InvariantCulture);
                    return true;
                case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                    formatted = d.ToString(CultureInfo.InvariantCulture);
                    return true;
                case decimal m:
                    formatted = m.ToString(CultureInfo.InvariantCulture);
                    return true;
                default:
                    formatted = string.Empty;
                    return false;
            }
        }

        private static bool TryToInt32(object? value, out int number)
        {
            switch (value)
            {
                case int i:
                    number = i;
                    return true;
                case char c:
                    number = c;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        private static bool TryToInt64(object? value, out long number)
        {
            switch (value)
            {
                case int i:
                    number = i;
                    return true;
                case long l:
                    number = l;
                    return true;
                case char c:
                    number = c;
                    return true;
                default:
                    number = 0L;
                    return false;
            }
        }

        private static bool TryToDouble(object? value, out double number)
        {
            switch (value)
            {
                case int i:
                    number = i;
                    return true;
                case long l:
                    number = l;
                    return true;
                case double d:
                    number = d;
                    return true;
                case char c:
                    number = c;
                    return true;
                default:
                    number = 0.0;
                    return false;
            }
        }

        private static bool TryToDecimal(object? value, out decimal number)
        {
            switch (value)
            {
                case int i:
                    number = i;
                    return true;
                case long l:
                    number = l;
                    return true;
                case decimal m:
                    number = m;
                    return true;
                case char c:
                    number = c;
                    return true;
                default:
                    number = 0m;
                    return false;
            }
        }
    }
}
