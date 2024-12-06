using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dorc.ApiModel.MonitorRunnerApi
{
    public class VariableValueSimple
    {
        public string Value { get; set; }
        public string FullTypeName { get; set; }
    }

    public class VariableValueJsonConverter : JsonConverter<VariableValue>
    {
        public override VariableValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var s = reader.GetString();

            var serialSimple = JsonSerializer.Deserialize<VariableValueSimple>(s);

            var type = Type.GetType(serialSimple.FullTypeName);

            if (type == null)
            {
                var err = $"Deserialized type failed for {serialSimple.FullTypeName} and value {serialSimple.Value}";
                Console.WriteLine(err);
                throw new Exception(err);
            }

            var value = JsonSerializer.Deserialize(serialSimple.Value, type);

            var variableValue = new VariableValue
            {
                Value = value,
                Type = type
            };

            return variableValue;
        }

        public override void Write(
            Utf8JsonWriter writer,
            VariableValue variableValue,
            JsonSerializerOptions options)
        {
            var stringType = variableValue.Type.FullName;
            var value = JsonSerializer.Serialize(variableValue.Value);
            var simple = new VariableValueSimple
            {
                Value = value,
                FullTypeName = stringType
            };
            var serialSimple = JsonSerializer.Serialize(simple);

            writer.WriteStringValue(serialSimple);
        }
    }
}
