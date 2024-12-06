using Dorc.ApiModel.MonitorRunnerApi;
using System.Text;

namespace Dorc.Core.VariableResolution
{
    public class PropertyEvaluator : IPropertyEvaluator
    {
        private readonly PropertyParser _parser = new();

        public VariableValue? Evaluate(IVariableResolver variableResolver, string value)
        {
            if (!variableResolver.PropertiesLoaded())
            {
                variableResolver.LoadProperties();
            }

            if (value == null) return null;

            var list = new List<VariableValue?>();

            try
            {
                foreach (var token in _parser.Parse(value)) 
                    switch (token)
                    {
                        case StaticToken _:
                            list.Add(new VariableValue { Value = token.Value, Type = token.Value.GetType() });
                            break;
                        case PropertyToken _:
                            {
                                var propertyValue = variableResolver.GetPropertyValue(token.Value);
                                if (propertyValue == null)
                                    return new VariableValue { Value = value, Type = value.GetType() };

                                list.Add(propertyValue);
                                break;
                            }
                    }
            }
            catch
            {
                return new VariableValue { Value = value, Type = value.GetType() }; ;
            }

            if (list.Count == 1) return list[0];

            var evaluated = new StringBuilder();

            foreach (var item in list) evaluated.Append(item.Value);

            return new VariableValue { Value = evaluated.ToString(), Type = value.GetType() };
        }
    }
}