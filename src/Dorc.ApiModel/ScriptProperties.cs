using Dorc.ApiModel.MonitorRunnerApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class ScriptProperties
    {
        public string ScriptPath { get; set; }

        /// <summary>
        /// The content baseline this script's bytes are verified against, or null when none has
        /// been recorded.
        /// </summary>
        /// <remarks>
        /// Travels with the path rather than with the group, because it is a property of one
        /// script. A group holds every script sharing a PowerShell version, and they do not
        /// share a baseline.
        /// </remarks>
        public string ExpectedContentHash { get; set; }

        public IDictionary<string, VariableValue> Properties { get; set; }
    }
}
