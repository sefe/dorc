namespace Dorc.Api.Services
{
    public class NonEnoughRightsException : ApplicationException
    {
        public NonEnoughRightsException(string message)
            : base(message)
        {
        }
    }
}