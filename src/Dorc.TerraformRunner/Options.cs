using CommandLine;

namespace Dorc.TerraformmRunner
{
    public class Options
    {
        [Option('p', "pipeName", Required = true, HelpText = "NamedPipeName to request paramters.")]
        public string PipeName { get; set; }

        [Option('l', "logPath", Required = true, HelpText = "Path to log file for the runner.")]
        public string LogPath { get; set; }

        [Option('s', "scriptPath", Required = false, HelpText = "Path to the scripts folder.")]
        public string ScriptPath { get; set; }

        [Option('r', "resultFilePath", Required = true, HelpText = "Terraform result file path")]
        public string ResultFilePath { get; set; }

        [Option('f', "useFile", Required = false, HelpText = "File be used instead of named pipe to get all script properties.")]
        public bool UseFile { get; set; }
    }
}