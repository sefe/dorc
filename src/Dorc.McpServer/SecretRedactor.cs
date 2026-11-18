using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Dorc.ApiModel;

namespace Dorc.McpServer;

public static class SecretRedactor
{
    public const string RedactedMarker = "[REDACTED]";

    private static readonly Regex SensitivePropertyNamePattern = new(
        @"(password|passwd|pwd|secret|token|apikey|api_key|clientsecret|client_secret|connectionstring|connection_string|privatekey|private_key|passphrase|credential| sas|sas_token)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsLikelySensitiveName(string? propertyName)
    {
        return !string.IsNullOrWhiteSpace(propertyName) &&
               SensitivePropertyNamePattern.IsMatch(propertyName);
    }

    public static List<PropertyValueDto> RedactPropertyValues(IEnumerable<PropertyValueDto>? items)
    {
        if (items is null)
        {
            return [];
        }

        var redacted = new List<PropertyValueDto>();
        foreach (var item in items)
        {
            var isSensitive = (item.Property?.Secure == true) || IsLikelySensitiveName(item.Property?.Name);
            redacted.Add(new PropertyValueDto
            {
                Id = item.Id,
                Value = isSensitive ? RedactedMarker : item.Value,
                Property = item.Property,
                PropertyValueFilter = item.PropertyValueFilter,
                PropertyValueFilterId = item.PropertyValueFilterId,
                Priority = item.Priority,
                UserEditable = item.UserEditable
            });
        }

        return redacted;
    }

    public static GetScopedPropertyValuesResponseDto RedactScopedPropertyValues(GetScopedPropertyValuesResponseDto? response)
    {
        if (response is null)
        {
            return new GetScopedPropertyValuesResponseDto { Items = [] };
        }

        var redactedItems = new List<FlatPropertyValueApiModel>();
        if (response.Items is not null)
        {
            foreach (var item in response.Items)
            {
                var isSensitive = item.Secure || IsLikelySensitiveName(item.Property);
                redactedItems.Add(new FlatPropertyValueApiModel
                {
                    PropertyId = item.PropertyId,
                    Property = item.Property,
                    PropertyValueScope = item.PropertyValueScope,
                    PropertyValueScopeId = item.PropertyValueScopeId,
                    PropertyValueId = item.PropertyValueId,
                    Secure = item.Secure,
                    IsArray = item.IsArray,
                    UserEditable = item.UserEditable,
                    PropertyValue = isSensitive && item.PropertyValue is not null ? RedactedMarker : item.PropertyValue,
                    PropertyArrayValue = isSensitive && item.PropertyArrayValue is not null
                        ? [RedactedMarker]
                        : item.PropertyArrayValue
                });
            }
        }

        return new GetScopedPropertyValuesResponseDto
        {
            CurrentPage = response.CurrentPage,
            TotalItems = response.TotalItems,
            TotalPages = response.TotalPages,
            Items = redactedItems
        };
    }

    public static List<PropertyValueScopeOptionApiModel> RedactScopeOptions(IEnumerable<PropertyValueScopeOptionApiModel>? options)
    {
        if (options is null)
        {
            return [];
        }

        var redacted = new List<PropertyValueScopeOptionApiModel>();
        foreach (var option in options)
        {
            redacted.Add(new PropertyValueScopeOptionApiModel
            {
                ValueOption = option.ValueOption,
                SampleResolvedValue = option.SampleResolvedValue is not null ? RedactedMarker : null
            });
        }

        return redacted;
    }

    public static GetPropertyValuesAuditListResponseDto RedactPropertyValuesAudit(GetPropertyValuesAuditListResponseDto? response)
    {
        if (response is null)
        {
            return new GetPropertyValuesAuditListResponseDto { Items = [] };
        }

        var redactedItems = new List<PropertyValueAuditApiModel>();
        if (response.Items is not null)
        {
            foreach (var item in response.Items)
            {
                redactedItems.Add(new PropertyValueAuditApiModel
                {
                    Id = item.Id,
                    PropertyId = item.PropertyId,
                    PropertyValueId = item.PropertyValueId,
                    PropertyName = item.PropertyName,
                    EnvironmentName = item.EnvironmentName,
                    FromValue = item.FromValue is not null ? RedactedMarker : null,
                    ToValue = item.ToValue is not null ? RedactedMarker : null,
                    UpdatedBy = item.UpdatedBy,
                    UpdatedDate = item.UpdatedDate,
                    Type = item.Type,
                    CanEdit = item.CanEdit,
                    CanDelete = item.CanDelete,
                    IsEnabled = item.IsEnabled
                });
            }
        }

        return new GetPropertyValuesAuditListResponseDto
        {
            CurrentPage = response.CurrentPage,
            TotalItems = response.TotalItems,
            TotalPages = response.TotalPages,
            Items = redactedItems
        };
    }

    public static List<BundledRequestsApiModel> RedactBundledRequests(IEnumerable<BundledRequestsApiModel>? items)
    {
        if (items is null)
        {
            return [];
        }

        var redacted = new List<BundledRequestsApiModel>();

        foreach (var item in items)
        {
            redacted.Add(new BundledRequestsApiModel
            {
                Id = item.Id,
                BundleName = item.BundleName,
                ProjectId = item.ProjectId,
                Type = item.Type,
                RequestName = item.RequestName,
                Sequence = item.Sequence,
                Request = RedactBundledRequestJson(item.Request)
            });
        }

        return redacted;
    }

    public static string? RedactBundledRequestJson(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return requestJson;
        }

        try
        {
            var rootNode = JsonNode.Parse(requestJson);
            if (rootNode is not JsonObject rootObj)
            {
                return RedactedMarker;
            }

            var matchingArrays = rootObj
                .Where(kvp => string.Equals(kvp.Key, "RequestProperties", StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Value)
                .OfType<JsonArray>()
                .ToList();

            foreach (var propsArray in matchingArrays)
            {
                foreach (var propObj in propsArray.OfType<JsonObject>())
                {
                    var propName = GetCaseInsensitiveProperty(propObj, "PropertyName")?.GetValue<string>();
                    var isSensitiveNode = GetCaseInsensitiveProperty(propObj, "IsSensitive");
                    var isSensitive = isSensitiveNode is not null &&
                                      (isSensitiveNode.GetValueKind() == JsonValueKind.True ||
                                       (isSensitiveNode.GetValueKind() == JsonValueKind.String &&
                                        bool.TryParse(isSensitiveNode.GetValue<string>(), out var parsed) &&
                                        parsed));

                    if (isSensitive || IsLikelySensitiveName(propName))
                    {
                        SetCaseInsensitiveProperty(propObj, "PropertyValue", RedactedMarker);
                    }
                }
            }

            return rootObj.ToJsonString();
        }
        catch (JsonException)
        {
            return RedactedMarker;
        }
    }

    public static string? RedactRequestDetailsXml(string? requestDetailsXml)
    {
        if (string.IsNullOrWhiteSpace(requestDetailsXml))
        {
            return requestDetailsXml;
        }

        try
        {
            var root = XElement.Parse(requestDetailsXml);
            foreach (var propertyPair in root.Descendants("PropertyPair"))
            {
                var nameElement = propertyPair.Element("Name");
                var sensitiveElement = propertyPair.Element("IsSensitive");
                var isFlaggedSensitive = sensitiveElement is not null &&
                                         bool.TryParse(sensitiveElement.Value, out var parsed) &&
                                         parsed;

                if (isFlaggedSensitive || IsLikelySensitiveName(nameElement?.Value))
                {
                    var valueElement = propertyPair.Element("Value");
                    if (valueElement is not null)
                    {
                        valueElement.Value = RedactedMarker;
                    }
                }
            }

            return root.ToString(SaveOptions.DisableFormatting);
        }
        catch (XmlException)
        {
            return RedactedMarker;
        }
    }

    private static JsonNode? GetCaseInsensitiveProperty(JsonObject obj, string propertyName)
    {
        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static void SetCaseInsensitiveProperty(JsonObject obj, string propertyName, string value)
    {
        foreach (var kvp in obj.ToList())
        {
            if (string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                obj[kvp.Key] = value;
                return;
            }
        }

        obj[propertyName] = value;
    }
}
