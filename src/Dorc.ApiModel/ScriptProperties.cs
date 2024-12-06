using Dorc.ApiModel.MonitorRunnerApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class ScriptProperties
    {
        public string ScriptPath { get; set; }
        public IDictionary<string, VariableValue> Properties { get; set; }
    }
}
