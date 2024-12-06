namespace Dorc.NetFramework.Runner
{
    internal interface IScriptGroupProcessor
    {
        void Process(string pipeName,int requestId);
    }
}
