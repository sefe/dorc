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
    /// The grammar fails closed. Anything it cannot parse is a
    /// <see cref="PropertyExpressionParseException"/> and never falls back to compilation. That
    /// is what contains the one part of the inventory that could not be examined — secure
    /// configuration values are encrypted at rest, could not be inspected by query, and do
    /// reach the evaluator.
    /// </summary>
    public static class PropertyExpressionGrammar
    {
        /// <summary>
        /// Evaluates an expression, with the <c>fn:</c> marker already stripped.
        /// </summary>
        /// <exception cref="PropertyExpressionParseException">
        /// The expression is outside the grammar. The message describes the *shape* of the
        /// failure and its position, and never quotes the literal's content — the literal is a
        /// resolved property value and may be a secret.
        /// </exception>
        public static string Evaluate(string expression)
        {
            if (string.IsNullOrEmpty(expression))
            {
                throw new PropertyExpressionParseException("the expression is empty.");
            }

            return new Parser(expression).Evaluate();
        }

        /// <summary>
        /// One evaluation of one expression. Owns the read position so that the parsing
        /// functions can be plain methods returning what they read, rather than threading the
        /// position and the result through reference parameters.
        /// </summary>
        private sealed class Parser
        {
            private readonly string _expression;
            private int _position;

            public Parser(string expression)
            {
                _expression = expression;
            }

            private bool AtEnd => _position >= _expression.Length;

            private char Current => _expression[_position];

            public string Evaluate()
            {
                var value = ReadStringLiteral();

                while (!AtEnd)
                {
                    value = ApplyOperation(value);
                }

                return value;
            }

            /// <summary>
            /// Reads a C# double-quoted literal, honouring the escapes that can legitimately
            /// appear in one. An unrecognised escape is a refusal rather than a passthrough: the
            /// point of this type is that nothing it does not understand gets evaluated.
            /// </summary>
            private string ReadStringLiteral()
            {
                SkipWhitespace();

                if (AtEnd || Current != '"')
                {
                    throw Refuse(
                        $"expected a double-quoted string literal at position {_position}."
                        + " Verbatim and interpolated literals are not part of the grammar.");
                }

                _position++;

                var literal = new StringBuilder();

                while (!AtEnd)
                {
                    var character = Current;

                    if (character == '"')
                    {
                        _position++;
                        return literal.ToString();
                    }

                    if (character != '\\')
                    {
                        literal.Append(character);
                        _position++;
                        continue;
                    }

                    _position++;
                    if (AtEnd)
                    {
                        throw Refuse("the string literal ends with an incomplete escape sequence.");
                    }

                    switch (Current)
                    {
                        case '\\': literal.Append('\\'); break;
                        case '"': literal.Append('"'); break;
                        case 'n': literal.Append('\n'); break;
                        case 'r': literal.Append('\r'); break;
                        case 't': literal.Append('\t'); break;
                        case '0': literal.Append('\0'); break;
                        default:
                            throw Refuse($"unsupported escape sequence at position {_position}.");
                    }

                    _position++;
                }

                throw Refuse("the string literal is not closed.");
            }

            private string ApplyOperation(string value)
            {
                SkipWhitespace();

                if (AtEnd)
                {
                    return value;
                }

                if (Current != '.')
                {
                    throw Refuse(
                        $"expected '.' before an operation at position {_position}, or the end of the expression.");
                }

                _position++;

                // The operation name is read as a whole identifier before being matched, not
                // prefix-matched. Prefix-matching "ToLower" against "ToLowerInvariant" would still
                // refuse the expression - correctly - but blame the argument list rather than the
                // unsupported operation, which sends whoever is diagnosing it the wrong way.
                var operation = ReadIdentifier();

                switch (operation)
                {
                    case "ToLower":
                        ConsumeEmptyArgumentList(operation);

                        // Invariant rather than current-culture. The Turkish dotless i is the
                        // standard example of why a deployment must not resolve a property
                        // differently depending on the host's locale.
                        return value.ToLowerInvariant();

                    case "ToUpper":
                        ConsumeEmptyArgumentList(operation);
                        return value.ToUpperInvariant();

                    case "Replace":
                        var (from, to) = ReadReplaceArguments();

                        if (from.Length == 0)
                        {
                            // Replacing the empty string is not something the estate does, and
                            // .NET throws for it. Refuse it here so the failure names the reason.
                            throw Refuse("Replace was called with an empty first argument.");
                        }

                        return value.Replace(from, to, StringComparison.Ordinal);

                    default:
                        throw Refuse(
                            $"unsupported operation at position {_position - operation.Length}."
                            + " The grammar allows ToLower, ToUpper and Replace only.");
                }
            }

            private string ReadIdentifier()
            {
                SkipWhitespace();

                var start = _position;
                while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
                {
                    _position++;
                }

                return _expression[start.._position];
            }

            private (string From, string To) ReadReplaceArguments()
            {
                Expect('(', $"expected '(' after Replace at position {_position}.");

                var from = ReadStringLiteral();

                SkipWhitespace();
                Expect(
                    ',',
                    $"expected ',' between Replace arguments at position {_position}."
                    + " Replace takes exactly two string literals.");

                var to = ReadStringLiteral();

                SkipWhitespace();
                Expect(')', $"expected ')' after the Replace arguments at position {_position}.");

                return (from, to);
            }

            private void ConsumeEmptyArgumentList(string operation)
            {
                SkipWhitespace();

                if (!AtEnd && Current == '(')
                {
                    _position++;
                    SkipWhitespace();

                    if (!AtEnd && Current == ')')
                    {
                        _position++;
                        return;
                    }
                }

                throw Refuse($"{operation} takes no arguments and must be written as {operation}().");
            }

            /// <summary>
            /// Consumes <paramref name="expected"/> at the current position, or refuses with the
            /// message. The message is built by the caller so that it reports the position at
            /// which the character was expected.
            /// </summary>
            private void Expect(char expected, string refusal)
            {
                SkipWhitespace();

                if (AtEnd || Current != expected)
                {
                    throw Refuse(refusal);
                }

                _position++;
            }

            private void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(Current))
                {
                    _position++;
                }
            }

            private static PropertyExpressionParseException Refuse(string reason) =>
                new PropertyExpressionParseException(reason);
        }
    }

    /// <summary>
    /// An expression outside the grammar. The message names the shape and position of the
    /// failure only; it never contains the expression's text.
    /// </summary>
    public sealed class PropertyExpressionParseException : InvalidOperationException
    {
        public PropertyExpressionParseException(string message)
            : base(message)
        {
        }
    }
}
