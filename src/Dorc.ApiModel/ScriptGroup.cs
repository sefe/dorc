using Dorc.ApiModel.MonitorRunnerApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class ScriptGroup
    {
        public Guid ID { get; set; }
        public int DeployResultId { get; set; }
        public string PowerShellVersionNumber { get; set; }
        public string ScriptsLocation { get; set; }

        public IDictionary<string, VariableValue> CommonProperties { get; set; }
        public IList<ScriptProperties> ScriptProperties { get; set; }
    }
}
