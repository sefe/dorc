namespace Dorc.Api.Windows.Exceptions
{
    public class NonEnoughRightsException : ApplicationException
    {
        public NonEnoughRightsException(string message)
            : base(message)
        {
        }
    }
}