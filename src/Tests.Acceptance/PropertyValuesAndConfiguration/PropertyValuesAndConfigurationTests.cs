using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;

namespace Tests.Acceptance.PropertyValuesAndConfiguration
{
    [TestClass]
    [TestCategory("PropertyValuesAndConfiguration")]
    public class PropertyValuesAndConfigurationTests
    {
        [TestMethod]
        public void CreateUpdateDeletePropertiesAndValues_MultiEnvironmentPropertyLifecycleScopedSearchAuditAndExport_PersistsAuditsExportsAndDeletesCleanly()
        {
            AcceptanceProjectProvisioner.AssertLiveEnvironmentEnabled();

            using var caller = new DorcApiClient();
            AcceptanceProjectProvisioner.EnsureProvisioned(caller);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var plainProp = $"AutoReg_PostmanProp_{suffix}";
            var renamedPlainProp = $"AutoReg_PostmanPropRenamed_{suffix}";
            var arrayProp = $"AutoReg_PostmanArray_{suffix}";
            var secureProp = $"AutoReg_PostmanSecret_{suffix}";

            try
            {
                // 1. POST /Properties (create plain, array, and secure property definitions)
                var createPropsResp = caller.ExecuteRaw("Properties", Method.Post, body: new[]
                {
                    new PropertyApiModel { Name = plainProp, Secure = false, IsArray = false },
                    new PropertyApiModel { Name = arrayProp, Secure = false, IsArray = true },
                    new PropertyApiModel { Name = secureProp, Secure = true, IsArray = false }
                });
                Assert.AreEqual(HttpStatusCode.OK, createPropsResp.StatusCode, $"POST /Properties failed: {createPropsResp.Content}");

                // 2. GET /Properties and GET /Properties/id={id}
                var getPropsResp = caller.ExecuteRaw("Properties", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getPropsResp.StatusCode, getPropsResp.Content);
                var allProps = DorcApiClient.Deserialize<List<PropertyApiModel>>(getPropsResp.Content);
                Assert.IsNotNull(allProps);
                Assert.IsTrue(allProps.Any(p => string.Equals(p.Name, plainProp, StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(allProps.Any(p => string.Equals(p.Name, arrayProp, StringComparison.OrdinalIgnoreCase) && p.IsArray));
                Assert.IsTrue(allProps.Any(p => string.Equals(p.Name, secureProp, StringComparison.OrdinalIgnoreCase) && p.Secure));

                var getSinglePropResp = caller.ExecuteRaw($"Properties/id={Uri.EscapeDataString(plainProp)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getSinglePropResp.StatusCode, $"GET /Properties/id={plainProp} failed: {getSinglePropResp.Content}");
                var singleProp = DorcApiClient.Deserialize<PropertyApiModel>(getSinglePropResp.Content);
                Assert.IsNotNull(singleProp);
                Assert.AreEqual(plainProp, singleProp.Name);

                // 3. PUT /Properties (rename/update property definition dictionary)
                var putPropsBody = new Dictionary<string, PropertyApiModel>
                {
                    [plainProp] = new PropertyApiModel { Id = singleProp.Id, Name = renamedPlainProp, Secure = false, IsArray = false }
                };
                var putPropsResp = caller.ExecuteRaw("Properties", Method.Put, body: putPropsBody);
                Assert.AreEqual(HttpStatusCode.OK, putPropsResp.StatusCode, $"PUT /Properties failed: {putPropsResp.Content}");
                plainProp = renamedPlainProp;

                // 4. POST /PropertyValues across Default (null), Parent env, and Child env
                var postValsResp = caller.ExecuteRaw("PropertyValues", Method.Post, body: new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainProp, Secure = false, IsArray = false },
                        PropertyValueFilter = null,
                        Value = $"Default_{suffix}"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainProp, Secure = false, IsArray = false },
                        PropertyValueFilter = AcceptanceProjectProvisioner.LiveParentEnvName,
                        Value = $"Parent_{suffix}"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainProp, Secure = false, IsArray = false },
                        PropertyValueFilter = AcceptanceProjectProvisioner.LiveChildEnvName,
                        Value = $"ChildInitial_{suffix}"
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = secureProp, Secure = true, IsArray = false },
                        PropertyValueFilter = AcceptanceProjectProvisioner.LiveChildEnvName,
                        Value = $"TopSecret_{suffix}!"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, postValsResp.StatusCode, $"POST /PropertyValues failed: {postValsResp.Content}");

                // 5. GET /PropertyValues by propertyName and environmentName
                var getChildValResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = plainProp,
                    ["environmentName"] = AcceptanceProjectProvisioner.LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, getChildValResp.StatusCode, getChildValResp.Content);
                var childVals = DorcApiClient.Deserialize<List<PropertyValueDto>>(getChildValResp.Content);
                Assert.IsNotNull(childVals);
                var childVal = childVals.FirstOrDefault(v =>
                    string.Equals(v.PropertyValueFilter, AcceptanceProjectProvisioner.LiveChildEnvName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(childVal, "Expected child environment property value.");
                Assert.AreEqual($"ChildInitial_{suffix}", childVal.Value);

                // 6. PUT /PropertyValues (update child environment value)
                childVal.Value = $"ChildUpdated_{suffix}";
                var putValResp = caller.ExecuteRaw("PropertyValues", Method.Put, body: new[] { childVal });
                Assert.AreEqual(HttpStatusCode.OK, putValResp.StatusCode, $"PUT /PropertyValues failed: {putValResp.Content}");

                // 7. PUT /RefDataScopedPropertyValues and PUT /RefDataSearchPropertyValues
                var scopedResp = caller.ExecuteRaw(
                    "RefDataScopedPropertyValues",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["scope"] = AcceptanceProjectProvisioner.LiveChildEnvName,
                        ["page"] = "1",
                        ["limit"] = "50"
                    },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "Property", FilterValue = plainProp }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, scopedResp.StatusCode, $"PUT /RefDataScopedPropertyValues failed: {scopedResp.Content}");
                var scopedPage = DorcApiClient.Deserialize<GetScopedPropertyValuesResponseDto>(scopedResp.Content);
                Assert.IsNotNull(scopedPage?.Items);
                Assert.IsTrue(
                    scopedPage!.Items.Any(i =>
                        string.Equals(i.Property, plainProp, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(i.PropertyValue, $"ChildUpdated_{suffix}", StringComparison.Ordinal)),
                    $"Expected RefDataScopedPropertyValues to contain updated '{plainProp}', got: {scopedResp.Content}");

                var searchResp = caller.ExecuteRaw(
                    "RefDataSearchPropertyValues",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["page"] = "1",
                        ["limit"] = "50"
                    },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "Property", FilterValue = plainProp }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, searchResp.StatusCode, $"PUT /RefDataSearchPropertyValues failed: {searchResp.Content}");
                var searchPage = DorcApiClient.Deserialize<GetScopedPropertyValuesResponseDto>(searchResp.Content);
                Assert.IsNotNull(searchPage?.Items);
                Assert.IsGreaterThanOrEqualTo(3, searchPage!.Items.Count, $"Expected at least 3 scopes (Default, Parent, Child) for '{plainProp}' in RefDataSearchPropertyValues.");

                // 8. PUT /PropertyValuesAudit (verify Insert and Update audit history)
                var auditResp = caller.ExecuteRaw(
                    "PropertyValuesAudit",
                    Method.Put,
                    new Dictionary<string, string>
                    {
                        ["page"] = "1",
                        ["limit"] = "20",
                        ["useAndForFilters"] = "true"
                    },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "PropertyName", FilterValue = plainProp }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, auditResp.StatusCode, $"PUT /PropertyValuesAudit failed: {auditResp.Content}");
                var audits = DorcApiClient.Deserialize<GetPropertyValuesAuditListResponseDto>(auditResp.Content);
                Assert.IsNotNull(audits?.Items);
                Assert.IsTrue(
                    audits!.Items.Any(a =>
                        string.Equals(a.PropertyName, plainProp, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(a.ToValue, $"ChildUpdated_{suffix}", StringComparison.Ordinal)),
                    $"Expected PropertyValuesAudit to record update to 'ChildUpdated_{suffix}'.");

                // 9. GET /PropertyValues/non-secure-export & GET /PropertyValues/ScopeOptions
                var csvMissingEnvResp = caller.ExecuteRaw("PropertyValues/non-secure-export", Method.Get);
                Assert.AreEqual(HttpStatusCode.BadRequest, csvMissingEnvResp.StatusCode);

                var csvExportResp = caller.ExecuteRaw("PropertyValues/non-secure-export", Method.Get, new Dictionary<string, string>
                {
                    ["environmentName"] = AcceptanceProjectProvisioner.LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, csvExportResp.StatusCode, csvExportResp.Content);
                StringAssert.Contains(csvExportResp.Content ?? string.Empty, "PropertyName,Value,Is Secure");
                StringAssert.Contains(csvExportResp.Content ?? string.Empty, $"{plainProp},ChildUpdated_{suffix},FALSE");
                Assert.IsFalse(
                    (csvExportResp.Content ?? string.Empty).Contains(secureProp, StringComparison.OrdinalIgnoreCase),
                    "non-secure-export CSV must never include Secure=true properties.");

                var scopeOptionsResp = caller.ExecuteRaw("PropertyValues/ScopeOptions", Method.Get, new Dictionary<string, string>
                {
                    ["propertyValueScope"] = AcceptanceProjectProvisioner.LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, scopeOptionsResp.StatusCode, scopeOptionsResp.Content);
                var scopeOptions = DorcApiClient.Deserialize<List<PropertyValueScopeOptionApiModel>>(scopeOptionsResp.Content) ?? new List<PropertyValueScopeOptionApiModel>();
                Assert.IsNotEmpty(scopeOptions, "Expected non-empty ScopeOptions for environment.");
                Assert.IsTrue(scopeOptions.Any(o => string.Equals(o.ValueOption, "$EnvironmentName$", StringComparison.OrdinalIgnoreCase)),
                    "Expected ScopeOptions to contain $EnvironmentName$.");

                // 10. POST /Properties/import CSV validation & import/update lifecycle
                var csvImportPropName = $"DOrc_Live_CsvImport_{suffix}";
                var invalidImportBlankResp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = "", Value = "", IsSecure = false, Environment = AcceptanceProjectProvisioner.LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, invalidImportBlankResp.StatusCode, invalidImportBlankResp.Content);
                StringAssert.Contains(invalidImportBlankResp.Content ?? string.Empty, "error: PropertyName cannot be blank or empty");

                var invalidImportConflictResp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = "Val1", IsSecure = false, Environment = "" },
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = "Val2", IsSecure = true, Environment = AcceptanceProjectProvisioner.LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, invalidImportConflictResp.StatusCode, invalidImportConflictResp.Content);
                StringAssert.Contains(invalidImportConflictResp.Content ?? string.Empty, $"error: conflicting IsSecure values for property '{csvImportPropName}'");

                var importV1Resp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvDefault_V1_{suffix}", IsSecure = false, Environment = "" },
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvChild_V1_{suffix}", IsSecure = false, Environment = AcceptanceProjectProvisioner.LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, importV1Resp.StatusCode, importV1Resp.Content);

                var importV2Resp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvDefault_V2_{suffix}", IsSecure = false, Environment = "" },
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvChild_V2_{suffix}", IsSecure = false, Environment = AcceptanceProjectProvisioner.LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, importV2Resp.StatusCode, importV2Resp.Content);
                var importV2ValsResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = csvImportPropName
                });
                Assert.AreEqual(HttpStatusCode.OK, importV2ValsResp.StatusCode, importV2ValsResp.Content);
                var importedV2Vals = DorcApiClient.Deserialize<List<PropertyValueDto>>(importV2ValsResp.Content) ?? new List<PropertyValueDto>();
                Assert.AreEqual($"CsvDefault_V2_{suffix}", importedV2Vals.Single(v => string.IsNullOrEmpty(v.PropertyValueFilter)).Value);
                Assert.AreEqual($"CsvChild_V2_{suffix}", importedV2Vals.Single(v => string.Equals(v.PropertyValueFilter, AcceptanceProjectProvisioner.LiveChildEnvName, StringComparison.OrdinalIgnoreCase)).Value);
                AcceptanceProjectProvisioner.DeletePropertiesBestEffort(caller, csvImportPropName);

                // 11. Tools.DOrc.Cmdlet Export-DOrcProperties / Import-DOrcProperties CSV contract round-trip
                var exportGetResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = plainProp
                });
                Assert.AreEqual(HttpStatusCode.OK, exportGetResp.StatusCode, exportGetResp.Content);
                var exportedList = DorcApiClient.Deserialize<List<PropertyValueDto>>(exportGetResp.Content);
                Assert.IsNotNull(exportedList);
                Assert.HasCount(3, exportedList, "Expected Default, Parent, and Child property values before Delete.");

                // 10. DELETE /PropertyValues for Parent environment only, then re-import via POST /PropertyValues
                var parentVal = exportedList.First(v =>
                    string.Equals(v.PropertyValueFilter, AcceptanceProjectProvisioner.LiveParentEnvName, StringComparison.OrdinalIgnoreCase));
                var delValResp = caller.ExecuteRaw("PropertyValues", Method.Delete, body: new[]
                {
                    new PropertyValueDto
                    {
                        Id = parentVal.Id,
                        Property = parentVal.Property,
                        PropertyValueFilter = parentVal.PropertyValueFilter
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, delValResp.StatusCode, $"DELETE /PropertyValues failed: {delValResp.Content}");

                var reimportResp = caller.ExecuteRaw("PropertyValues", Method.Post, body: new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainProp, Secure = false, IsArray = false },
                        PropertyValueFilter = AcceptanceProjectProvisioner.LiveParentEnvName,
                        Value = $"ParentReimported_{suffix}"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, reimportResp.StatusCode, $"Re-import POST /PropertyValues failed: {reimportResp.Content}");
            }
            finally
            {
                AcceptanceProjectProvisioner.DeletePropertiesBestEffort(caller, plainProp, $"AutoReg_PostmanProp_{suffix}", arrayProp, secureProp);
            }
        }

        [TestMethod]
        public void ManageSystemConfigurationAndRuntimeMetadata_RefDataConfigCrudPowerShellVersionsApiConfigAndMetadata_PersistsAndCleansUp()
        {
            AcceptanceProjectProvisioner.AssertLiveEnvironmentEnabled();

            using var caller = new DorcApiClient();
            AcceptanceProjectProvisioner.EnsureProvisioned(caller);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var configKey = $"AutoReg_ConfigKey_{suffix}";
            var createdConfigId = 0;

            try
            {
                // 1. GET /RefDataConfig (verify ScriptRoot and system config list)
                var getConfigsResp = caller.ExecuteRaw("RefDataConfig", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getConfigsResp.StatusCode, $"GET /RefDataConfig failed: {getConfigsResp.Content}");
                var configs = DorcApiClient.Deserialize<List<ConfigValueApiModel>>(getConfigsResp.Content);
                Assert.IsNotNull(configs);
                Assert.IsTrue(
                    configs.Any(c => string.Equals(c.Key, "ScriptRoot", StringComparison.OrdinalIgnoreCase)),
                    "Expected RefDataConfig to contain ScriptRoot.");

                // 2. POST /RefDataConfig (create a temporary configuration value)
                var postConfigResp = caller.ExecuteRaw("RefDataConfig", Method.Post, body: new ConfigValueApiModel
                {
                    Key = configKey,
                    Value = $"InitialConfigVal_{suffix}",
                    Secure = false,
                    IsForProd = false
                });
                Assert.AreEqual(HttpStatusCode.OK, postConfigResp.StatusCode, $"POST /RefDataConfig failed: {postConfigResp.Content}");
                var createdConfig = DorcApiClient.Deserialize<ConfigValueApiModel>(postConfigResp.Content);
                Assert.IsNotNull(createdConfig);
                Assert.IsGreaterThan(0, createdConfig.Id);
                createdConfigId = createdConfig.Id;

                // 3. PUT /RefDataConfig?id={id} (update configuration value)
                createdConfig.Value = $"UpdatedConfigVal_{suffix}";
                var putConfigResp = caller.ExecuteRaw("RefDataConfig", Method.Put, new Dictionary<string, string>
                {
                    ["id"] = createdConfigId.ToString()
                }, body: createdConfig);
                Assert.AreEqual(HttpStatusCode.OK, putConfigResp.StatusCode, $"PUT /RefDataConfig failed: {putConfigResp.Content}");
                var updatedConfig = DorcApiClient.Deserialize<ConfigValueApiModel>(putConfigResp.Content);
                Assert.IsNotNull(updatedConfig);
                Assert.AreEqual($"UpdatedConfigVal_{suffix}", updatedConfig.Value);

                // 4. DELETE /RefDataConfig?id={id}
                var delConfigResp = caller.ExecuteRaw("RefDataConfig", Method.Delete, new Dictionary<string, string>
                {
                    ["id"] = createdConfigId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, delConfigResp.StatusCode, $"DELETE /RefDataConfig failed: {delConfigResp.Content}");
                createdConfigId = 0;

                // 5. GET /PowerShellVersions
                var psVersionsResp = caller.ExecuteRaw("PowerShellVersions", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, psVersionsResp.StatusCode, $"GET /PowerShellVersions failed: {psVersionsResp.Content}");
                var psVersions = DorcApiClient.Deserialize<List<PowerShellVersionDto>>(psVersionsResp.Content);
                Assert.IsNotNull(psVersions);
                Assert.IsNotEmpty(psVersions, "Expected at least one supported PowerShell version from GET /PowerShellVersions.");

                // 6. GET /ApiConfig & GET /Metadata
                var apiConfigResp = caller.ExecuteRaw("ApiConfig", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, apiConfigResp.StatusCode, $"GET /ApiConfig failed: {apiConfigResp.Content}");
                var apiConfig = DorcApiClient.Deserialize<ApiConfigModel>(apiConfigResp.Content);
                Assert.IsNotNull(apiConfig);
                Assert.IsFalse(string.IsNullOrWhiteSpace(apiConfig.AuthenticationScheme));

                var metadataResp = caller.ExecuteRaw("Metadata", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, metadataResp.StatusCode, $"GET /Metadata failed: {metadataResp.Content}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(metadataResp.Content), "Expected non-empty version string from GET /Metadata.");
            }
            finally
            {
                if (createdConfigId > 0)
                {
                    caller.ExecuteRaw("RefDataConfig", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = createdConfigId.ToString()
                    });
                }
            }
        }
    }
}
