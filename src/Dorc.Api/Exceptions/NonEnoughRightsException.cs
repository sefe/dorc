namespace Dorc.Api.Exceptions
{
    public class NonEnoughRightsException : ApplicationException
    {
        public NonEnoughRightsException(string message)
            : base(message)
        {
        }
    }
}