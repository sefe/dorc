using System.Text.RegularExpressions;
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
}
