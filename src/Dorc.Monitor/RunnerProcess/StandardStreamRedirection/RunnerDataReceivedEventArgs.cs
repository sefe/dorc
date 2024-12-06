namespace Dorc.Monitor.RunnerProcess.StandardStreamRedirection
{
    internal class RunnerDataReceivedEventArgs : EventArgs
    {
        internal string? data;

        internal RunnerDataReceivedEventArgs(string? data)
        {
            this.data = data;
        }

        public string? Data
        {
            get
            {
                return data;
            }
        }
    }
}
