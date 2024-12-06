using CommandLine;

namespace Dorc.Runner
{
    public class Options
    {
        [Option('p', "pipeName", Required = true, HelpText = "NamedPipeName to request paramters.")]
        public string PipeName { get; set; } = null!;
    }
}