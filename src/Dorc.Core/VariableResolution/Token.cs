namespace Dorc.Core.VariableResolution
{
    public abstract class Token
    {
        public string Value { get; set; }

        public override int GetHashCode()
        {
            return GetType().GetHashCode() + Value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var other = obj as Token;
            if (other == null) return false;
            return GetType().Equals(other.GetType()) && Value.Equals(other.Value);
        }

        public override string ToString()
        {
            return $"{GetType().Name}:{Value}";
        }
    }

    public class StaticToken : Token
    {
        public StaticToken(string value)
        {
            Value = value;
        }
    }

    public class PropertyToken : Token
    {
        public PropertyToken(string value)
        {
            Value = value;
        }
    }
}