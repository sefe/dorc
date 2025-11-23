namespace Dorc.Api.Windows.Services
{
    public class NonEnoughRightsException : ApplicationException
    {
        public NonEnoughRightsException(string message)
            : base(message)
        {
        }
    }
}