namespace Dorc.Core.Exceptions
{
    [Serializable]
    public class ArtifactNotFoundException : Exception
    {
        public ArtifactNotFoundException() : base("Can't find artifact, ensure that it was published correctly in Azure DevOps")
        {
        }

        public ArtifactNotFoundException(string message) : base(message)
        {
        }

        public ArtifactNotFoundException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}