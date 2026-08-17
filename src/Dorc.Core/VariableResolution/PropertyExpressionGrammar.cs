using System.Text;

namespace Dorc.Core.VariableResolution
{
    /// <summary>
    /// Parses and evaluates the whole of the expression language DOrc's property values
    /// actually use.
    ///
    /// An estate inventory found 11 distinct expressions across 133 property values, every one
    /// of a single shape: a double-quoted string literal followed by a left-to-right chain of
    /// lower-casing, upper-casing and replacement. No loops, no reflection, no assembly
    /// references, no type construction, no input or output. One expression accounts for 101 of
    /// the 133 occurrences.
    ///
    /// That is a parser, not a compiler — so the C# scripting dependency is removed from this
    /// assembly rather than restricted. A compiler reachable from deployment data is a class of
    /// weakness; a parser for three operations is not.
    ///
    /// Token interpolation is NOT handled here, deliberately. It has already happened by the
    /// time an expression reaches this point, so the literal being parsed is the substituted
    /// text. That is also why parsing has to be strict: a substituted value containing a quote
    /// makes the literal's extent ambiguous, and an ambiguous expression is refused rather than
    /// guessed at.
    ///
    /// The grammar fails closed. Anything it cannot parse is an error and never falls back to
    /// compilation. That is what contains the one part of the inventory that could not be
    /// examined — secure configuration values are encrypted at rest, could not be inspected by
    /// query, and do reach the evaluator.
    /// </summary>
    public static class PropertyExpressionGrammar
    {
        /// <summary>
        /// Evaluates an expression, with the <c>fn:</c> marker already stripped.
        /// </summary>
        /// <param name="result">The evaluated string, when this returns true.</param>
        /// <param name="error">
        /// Why parsing failed, when this returns false. Deliberately describes the *shape* of
        /// the failure and its position, and never quotes the literal's content — the literal
        /// is a resolved property value and may be a secret.
        /// </param>
        public static bool TryEvaluate(string expression, out string result, out string error)
        {
            result = string.Empty;
            error = string.Empty;

            if (string.IsNullOrEmpty(expression))
            {
                error = "the expression is empty.";
                return false;
            }

            var position = 0;

            if (!TryReadStringLiteral(expression, ref position, out var value, out error))
            {
                return false;
            }

            while (position < expression.Length)
            {
                if (!TryApplyOperation(expression, ref position, ref value, out error))
                {
                    return false;
                }
            }

            result = value;
            return true;
        }

        /// <summary>
        /// Reads a C# double-quoted literal, honouring the escapes that can legitimately appear
        /// in one. An unrecognised escape is a refusal rather than a passthrough: the point of
        /// this type is that nothing it does not understand gets evaluated.
        /// </summary>
        private static bool TryReadStringLiteral(string expression, ref int position, out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;

            SkipWhitespace(expression, ref position);

            if (position >= expression.Length || expression[position] != '"')
            {
                error = $"expected a double-quoted string literal at position {position}."
                    + " Verbatim and interpolated literals are not part of the grammar.";
                return false;
            }

            position++;

            var literal = new StringBuilder();

            while (position < expression.Length)
            {
                var character = expression[position];

                if (character == '"')
                {
                    position++;
                    value = literal.ToString();
                    return true;
                }

                if (character != '\\')
                {
                    literal.Append(character);
                    position++;
                    continue;
                }

                position++;
                if (position >= expression.Length)
                {
                    error = "the string literal ends with an incomplete escape sequence.";
                    return false;
                }

                switch (expression[position])
                {
                    case '\\': literal.Append('\\'); break;
                    case '"': literal.Append('"'); break;
                    case 'n': literal.Append('\n'); break;
                    case 'r': literal.Append('\r'); break;
                    case 't': literal.Append('\t'); break;
                    case '0': literal.Append('\0'); break;
                    default:
                        error = $"unsupported escape sequence at position {position}.";
                        return false;
                }

                position++;
            }

            error = "the string literal is not closed.";
            return false;
        }

        private static bool TryApplyOperation(string expression, ref int position, ref string value, out string error)
        {
            error = string.Empty;

            SkipWhitespace(expression, ref position);

            if (position >= expression.Length)
            {
                return true;
            }

            if (expression[position] != '.')
            {
                error = $"expected '.' before an operation at position {position}, or the end of the expression.";
                return false;
            }

            position++;

            // The operation name is read as a whole identifier before being matched, not
            // prefix-matched. Prefix-matching "ToLower" against "ToLowerInvariant" would still
            // refuse the expression - correctly - but blame the argument list rather than the
            // unsupported operation, which sends whoever is diagnosing it the wrong way.
            var operation = ReadIdentifier(expression, ref position);

            switch (operation)
            {
                case "ToLower":
                    if (!TryConsumeEmptyArgumentList(expression, ref position, operation, out error))
                    {
                        return false;
                    }

                    // Invariant rather than current-culture. The Turkish dotless i is the
                    // standard example of why a deployment must not resolve a property
                    // differently depending on the host's locale.
                    value = value.ToLowerInvariant();
                    return true;

                case "ToUpper":
                    if (!TryConsumeEmptyArgumentList(expression, ref position, operation, out error))
                    {
                        return false;
                    }

                    value = value.ToUpperInvariant();
                    return true;

                case "Replace":
                    if (!TryReadReplaceArguments(expression, ref position, out var from, out var to, out error))
                    {
                        return false;
                    }

                    if (from.Length == 0)
                    {
                        // Replacing the empty string is not something the estate does, and .NET
                        // throws for it. Refuse it here so the failure names the reason.
                        error = "Replace was called with an empty first argument.";
                        return false;
                    }

                    value = value.Replace(from, to, StringComparison.Ordinal);
                    return true;

                default:
                    error = $"unsupported operation at position {position - operation.Length}."
                        + " The grammar allows ToLower, ToUpper and Replace only.";
                    return false;
            }
        }

        private static string ReadIdentifier(string expression, ref int position)
        {
            SkipWhitespace(expression, ref position);

            var start = position;
            while (position < expression.Length
                && (char.IsLetterOrDigit(expression[position]) || expression[position] == '_'))
            {
                position++;
            }

            return expression[start..position];
        }

        private static bool TryReadReplaceArguments(
            string expression, ref int position, out string from, out string to, out string error)
        {
            from = string.Empty;
            to = string.Empty;

            SkipWhitespace(expression, ref position);

            if (position >= expression.Length || expression[position] != '(')
            {
                error = $"expected '(' after Replace at position {position}.";
                return false;
            }

            position++;

            if (!TryReadStringLiteral(expression, ref position, out from, out error))
            {
                return false;
            }

            SkipWhitespace(expression, ref position);

            if (position >= expression.Length || expression[position] != ',')
            {
                error = $"expected ',' between Replace arguments at position {position}."
                    + " Replace takes exactly two string literals.";
                return false;
            }

            position++;

            if (!TryReadStringLiteral(expression, ref position, out to, out error))
            {
                return false;
            }

            SkipWhitespace(expression, ref position);

            if (position >= expression.Length || expression[position] != ')')
            {
                error = $"expected ')' after the Replace arguments at position {position}.";
                return false;
            }

            position++;
            return true;
        }

        private static bool TryConsumeEmptyArgumentList(
            string expression, ref int position, string operation, out string error)
        {
            error = string.Empty;

            SkipWhitespace(expression, ref position);

            if (position < expression.Length && expression[position] == '(')
            {
                position++;
                SkipWhitespace(expression, ref position);

                if (position < expression.Length && expression[position] == ')')
                {
                    position++;
                    return true;
                }
            }

            error = $"{operation} takes no arguments and must be written as {operation}().";
            return false;
        }

        private static void SkipWhitespace(string expression, ref int position)
        {
            while (position < expression.Length && char.IsWhiteSpace(expression[position]))
            {
                position++;
            }
        }
    }
}
