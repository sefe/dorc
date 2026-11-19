using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
﻿using Org.OpenAPITools.Client;

namespace Dorc.AzureDevOps.Client
{
    /// <summary>
    /// Builds the <see cref="ApiClient"/> the generated Azure DevOps API
    /// classes are constructed with, so every request deserializes through
    /// <see cref="AzureDevOpsListResponseConverter"/> and tolerates newly
    /// introduced Azure DevOps enum values (such as <c>"abandoned"</c> on
    /// <c>Build.Status</c>) that are absent from the 6.0 OpenAPI specification.
    /// Pass the same instance as both the synchronous and asynchronous client:
    /// <c>new BuildsApi(apiClient, apiClient, configuration)</c>.
    /// </summary>
    public static class AzureDevOpsApiClientFactory
    {
        public static ApiClient Create(string basePath)
        {
            var apiClient = new ApiClient(basePath);
            apiClient.SerializerSettings.ContractResolver = new AzureDevOpsContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    OverrideSpecifiedNames = false
                }
            };
            apiClient.SerializerSettings.Converters.Add(new AzureDevOpsListResponseConverter());
            return apiClient;
        }
    }

    /// <summary>
    /// Replaces the generated <see cref="StringEnumConverter"/> on OpenAPI
    /// enum models with <see cref="TolerantStringEnumConverter"/> so that enum
    /// strings added by newer Azure DevOps server versions (for example
    /// <c>"status": "abandoned"</c> on a build) do not abort deserialization of
    /// an entire build or definition list.
    /// </summary>
    public sealed class AzureDevOpsContractResolver : DefaultContractResolver
    {
        private static readonly JsonConverter TolerantEnumConverterInstance = new TolerantStringEnumConverter();

        protected override JsonConverter? ResolveContractConverter(Type objectType)
        {
            var enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            if (enumType.IsEnum)
            {
                return TolerantEnumConverterInstance;
            }

            return base.ResolveContractConverter(objectType);
        }

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (property.PropertyType != null)
            {
                var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (enumType.IsEnum && property.Converter is StringEnumConverter and not TolerantStringEnumConverter)
                {
                    property.Converter = TolerantEnumConverterInstance;
                }
            }

            return property;
        }
    }

    /// <summary>
    /// Deserializes known enum values through <see cref="StringEnumConverter"/>,
    /// falling back to <see langword="null"/> for nullable enums (or the default
    /// enum member for non-nullable enums) when Azure DevOps returns a string or
    /// integer outside the generated OpenAPI 6.0 enum definition.
    /// </summary>
    public sealed class TolerantStringEnumConverter : StringEnumConverter
    {
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var isNullable = Nullable.GetUnderlyingType(objectType) != null;
            var enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;

            try
            {
                return base.ReadJson(reader, objectType, existingValue, serializer);
            }
            catch (JsonSerializationException)
            {
                return isNullable ? null : Activator.CreateInstance(enumType);
            }
        }
    }
}
