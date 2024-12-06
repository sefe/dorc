using System.Text;

namespace Dorc.Core.VariableResolution
{
    public class PropertyParser
    {
        private const char EscapeCharacter = '$';

        public IEnumerable<Token> Parse(string value)
        {
            var state = ParseState.Static;
            var currentValue = new StringBuilder();

            foreach (var c in value)
                switch (c)
                {
                    case EscapeCharacter:
                        switch (state)
                        {
                            case ParseState.Static:
                                state = ParseState.Escape;
                                break;

                            case ParseState.Escape:
                                currentValue.Append(EscapeCharacter);
                                state = ParseState.Static;
                                break;

                            case ParseState.Property:
                                yield return new PropertyToken(currentValue.ToString());
                                currentValue = new StringBuilder();
                                state = ParseState.Static;
                                break;
                        }

                        break;

                    default:
                        switch (state)
                        {
                            case ParseState.Escape:
                                if (currentValue.Length > 0)
                                {
                                    yield return new StaticToken(currentValue.ToString());
                                    currentValue = new StringBuilder();
                                }

                                currentValue.Append(c);
                                state = ParseState.Property;
                                break;

                            default:
                                currentValue.Append(c);
                                break;
                        }

                        break;
                }

            if (state != ParseState.Static)
                throw new InvalidOperationException($"Cannot parse property string '{value}'");

            if (currentValue.Length > 0)
                yield return new StaticToken(currentValue.ToString());
        }

        private enum ParseState
        {
            Static,
            Escape,
            Property
        }
    }
}