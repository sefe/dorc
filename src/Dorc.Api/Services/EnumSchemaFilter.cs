using System.Linq;
using Dorc.ApiModel;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Dorc.Api.Services
{
    /// <summary>
    /// Schema filter that converts enum types to string representations in OpenAPI schema
    /// and propagates [TagSet] per-item length constraints to array item schemas.
    /// </summary>
    public class EnumSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type.IsEnum)
            {
                schema.Enum.Clear();
                schema.Type = "string";
                foreach (var name in Enum.GetNames(context.Type))
                {
                    schema.Enum.Add(new OpenApiString(name));
                }
            }

            if (schema.Items != null &&
                context.MemberInfo?.GetCustomAttributes(typeof(TagSetAttribute), inherit: true).Any() == true)
            {
                schema.Items.MaxLength = TagLimits.MaxTagLength;
            }
        }
    }
}
