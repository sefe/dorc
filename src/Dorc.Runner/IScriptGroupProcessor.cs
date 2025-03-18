namespace Dorc.Runner
{
    internal interface IScriptGroupProcessor
    {
        int Process(string pipeName, int requestId);
    }
}
