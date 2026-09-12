using Dorc.ApiModel;

namespace Dorc.Monitor.Pipes
{
    public interface IScriptGroupPipeServer
    {
        /// <summary>
        /// Makes the script group available to the Runner process that will be started as
        /// <paramref name="readerIdentity"/>. The script group carries resolved deployment
        /// properties, which include secrets, so implementations must make it reachable by
        /// that principal and no-one else.
        /// </summary>
        Task Start(
            string pipeName,
            ScriptGroup scriptGroup,
            ScriptGroupReaderIdentity readerIdentity,
            CancellationToken cancellationToken);

        /// <summary>
        /// Withdraws the script group once the Runner has finished with it. Callers invoke
        /// this from a finally block, so implementations must not throw.
        /// </summary>
        void Expire(string pipeName);
    }
}
