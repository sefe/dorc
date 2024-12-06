namespace Dorc.Core.Exceptions
{
    [Serializable]
    public class WrongComponentsException : Exception
    {
        public WrongComponentsException()
        {

        }
        public WrongComponentsException(string message) : base(message)
        {

        }

        public WrongComponentsException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}
