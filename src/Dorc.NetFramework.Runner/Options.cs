﻿using CommandLine;

namespace Dorc.NetFramework.Runner
{
    public class Options
    {
        [Option('p', "pipeName", Required = true, HelpText = "NamedPipeName to request paramters.")]
        public string PipeName { get; set; }

        [Option('f', "useFile", Required = false, HelpText = "File be used instead of named pipe to get all script properties.")]
        public bool UseFile { get; set; }
    }
}