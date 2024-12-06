using System;
using System.Text.Json.Serialization;

namespace Dorc.ApiModel.MonitorRunnerApi
{
    [JsonConverter(typeof(VariableValueJsonConverter))]
    public class VariableValue
    {
        private object _value;

        public object Value
        {
            get => _value;
            set
            {
                if (value is VariableValue)
                    throw new Exception("circular creation");
                if (value is string s && s.Contains("VariableValue"))
                    throw new Exception("set of object as string");
                _value = value;
            }
        }

        public Type Type { get; set; }
    }
}
