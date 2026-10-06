namespace Dorc.PersistentData.Exceptions
{
    [Serializable]
    public class DaemonDuplicateException : Exception
    {
        public DaemonDuplicateException()
        {
        }

        public DaemonDuplicateException(string message) : base(message)
        {
        }

        public DaemonDuplicateException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
