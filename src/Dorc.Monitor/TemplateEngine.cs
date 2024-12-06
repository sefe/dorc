using System.Collections;
using Commons.Collections;
using NVelocity;
using NVelocity.App;

namespace Dorc.Monitor
{
    public class TemplateEngine
    {
        private readonly Template _template;

        public TemplateEngine(ExtendedProperties templateEngineProperties, string templateResourcePath)
        {
            var engine = new VelocityEngine(templateEngineProperties);
            _template = engine.GetTemplate(templateResourcePath);
        }

        public string Execute(IDictionary<string, object> parameters)
        {
            if (parameters == null) throw new ArgumentNullException("parameters");
            var hashtable = new Hashtable(parameters.Count);
            foreach (var entry in parameters) hashtable.Add(entry.Key, entry.Value);

            var context = new VelocityContext(hashtable);

            using (var writer = new StringWriter())
            {
                _template.Merge(context, writer);

                return writer.GetStringBuilder().ToString();
            }
        }
    }
}