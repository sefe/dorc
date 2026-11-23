using System.Net;
using Dorc.ApiModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RestSharp;
using Tests.Acceptance.Support;
using static Tests.Acceptance.Support.AcceptanceProjectProvisioner;

namespace Tests.Acceptance.PowerShellRunnerExecution
{
    [TestClass]
    [TestCategory("PowerShellRunnerExecution")]
    public class PowerShellRunnerExecutionTests
    {
        [TestInitialize]
        public void EnsureLiveEnvironmentEnabled() => AssertLiveEnvironmentEnabled();

        [TestMethod]
        public void CreateDeploymentRequest_UpdatedPropertyHierarchyInheritanceAndSecretRedaction_InjectsResolvedValuesIntoRunner()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var correlationId = Guid.NewGuid().ToString("N")[..10];

            var globalProp = $"AutoReg_Global_{correlationId}";
            var parentProp = $"AutoReg_ParentInherited_{correlationId}";
            var childUpdatedProp = $"AutoReg_ChildUpdated_{correlationId}";
            var overrideProp = $"AutoReg_RequestOverride_{correlationId}";
            var secretProp = $"AutoReg_SecretPassword_{correlationId}";
            var correlationProp = "AutoReg_CorrelationId";

            var expectedGlobalVal = $"GlobalVal_{correlationId}";
            var expectedParentVal = $"ParentInheritedVal_{correlationId}";
            var initialChildVal = $"InitialChildVal_{correlationId}";
            var updatedChildVal = $"UpdatedChildVal_{correlationId}";
            var defaultOverrideVal = $"DefaultEnvVal_{correlationId}";
            var expectedOverrideVal = $"OverriddenAtRequest_{correlationId}";
            var cleartextSecret = $"Sup3rS3cr3tValu3_{correlationId}!";

            try
            {
                EnsurePropertyDefinition(caller, globalProp, secure: false);
                EnsurePropertyDefinition(caller, parentProp, secure: false);
                EnsurePropertyDefinition(caller, childUpdatedProp, secure: false);
                EnsurePropertyDefinition(caller, overrideProp, secure: false);
                EnsurePropertyDefinition(caller, secretProp, secure: true);
                EnsurePropertyDefinition(caller, correlationProp, secure: false);

                UpsertPropertyValue(caller, globalProp, secure: false, envFilter: null, value: expectedGlobalVal);
                UpsertPropertyValue(caller, parentProp, secure: false, envFilter: LiveParentEnvName, value: expectedParentVal);
                UpsertPropertyValue(caller, childUpdatedProp, secure: false, envFilter: LiveChildEnvName, value: initialChildVal);
                UpsertPropertyValue(caller, overrideProp, secure: false, envFilter: LiveChildEnvName, value: defaultOverrideVal);
                UpsertPropertyValue(caller, secretProp, secure: true, envFilter: LiveChildEnvName, value: cleartextSecret);
                UpsertPropertyValue(caller, correlationProp, secure: false, envFilter: LiveChildEnvName, value: correlationId);

                UpsertPropertyValue(caller, childUpdatedProp, secure: false, envFilter: LiveChildEnvName, value: updatedChildVal);

                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string> { VerifyPropsComponentName },
                    RequestProperties = new List<RequestProperty>
                    {
                        new RequestProperty
                        {
                            PropertyName = overrideProp,
                            PropertyValue = expectedOverrideVal
                        }
                    }
                };

                var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
                Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
                var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
                Assert.IsNotNull(created, $"Unexpected POST /Request body: {createResp.Content}");
                Assert.IsGreaterThan(0, created.Id, $"Expected positive RequestId, got {created.Id} ({createResp.Content})");

                var finalRequest = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
                Assert.HasCount(1, results, $"Expected 1 component result for request {created.Id}.");

                var resultLogResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString(),
                    ["resultId"] = results[0].Id.ToString()
                });
                var resultLogText = !string.IsNullOrWhiteSpace(resultLogResp.Content)
                    ? resultLogResp.Content
                    : (results[0].Log ?? string.Empty);

                Assert.AreEqual(
                    "Completed",
                    finalRequest.Status,
                    $"Expected request {created.Id} to reach Completed, but got '{finalRequest.Status}'. Result status='{results[0].Status}', previewLog='{results[0].Log}', fullLog='{resultLogText}'");

                var combinedLog = $"{results[0].Log}\n{resultLogText}";
                StringAssert.Contains(
                    combinedLog,
                    "VERIFIED_ALL_PROPERTIES_IN_RUNNER",
                    $"Expected live runner to verify all resolved properties in-process. Full log: {combinedLog}");
                StringAssert.Contains(
                    combinedLog,
                    "[REDACTED]",
                    $"Expected PowerShellScriptRunner to redact secret value to '[REDACTED]' in stdout log. Full log: {combinedLog}");

                var requestRaw = caller.ExecuteRaw("RequestStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.IsFalse(
                    (requestRaw.Content ?? string.Empty).Contains(cleartextSecret, StringComparison.Ordinal),
                    "Cleartext secret leaked in GET /RequestStatuses response.");
                Assert.IsFalse(
                    (resultsResp.Content ?? string.Empty).Contains(cleartextSecret, StringComparison.Ordinal),
                    "Cleartext secret leaked in GET /ResultStatuses response.");
                Assert.IsFalse(
                    resultLogText.Contains(cleartextSecret, StringComparison.Ordinal),
                    "Cleartext secret leaked in GET /ResultStatuses/Log response.");

                var runnerLogDir = @"C:\Log\DOrc\Deploy\Services\Requests";
                if (Directory.Exists(runnerLogDir))
                {
                    try
                    {
                        foreach (var logFile in Directory.GetFiles(runnerLogDir, $"*{created.Id}*"))
                        {
                            using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var sr = new StreamReader(fs);
                            var fileText = sr.ReadToEnd();
                            Assert.IsFalse(
                                fileText.Contains(cleartextSecret, StringComparison.Ordinal),
                                $"Cleartext secret leaked in runner log file '{logFile}'.");
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            finally
            {
                DeletePropertiesBestEffort(caller, globalProp, parentProp, childUpdatedProp, overrideProp, secretProp);
            }
        }

        [TestMethod]
        public void CreateDeploymentRequest_NetFrameworkRunnerPowerShell51_ExecutesDesktopEditionAndInjectsProperties()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var psVersionsResp = caller.ExecuteRaw("PowerShellVersions", Method.Get);
            Assert.AreEqual(HttpStatusCode.OK, psVersionsResp.StatusCode, psVersionsResp.Content);
            StringAssert.Contains(psVersionsResp.Content ?? string.Empty, "v5.1");

            var requestPayload = new RequestDto
            {
                Project = LiveProjectName,
                Environment = LiveChildEnvName,
                BuildNum = BuildFolderName,
                BuildUrl = buildUri,
                Components = new List<string> { NetFxRunnerComponentName }
            };

            var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
            Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
            var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
            Assert.IsNotNull(created);
            Assert.IsGreaterThan(0, created.Id);

            var finalState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
            var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString()
            });
            Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
            var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
            Assert.HasCount(1, results);

            var resultLogResp = caller.ExecuteRaw("ResultStatuses/Log", Method.Get, new Dictionary<string, string>
            {
                ["requestId"] = created.Id.ToString(),
                ["resultId"] = results[0].Id.ToString()
            });
            var combinedLog = $"{results[0].Log}\n{resultLogResp.Content}";

            Assert.AreEqual(
                "Completed",
                finalState.Status,
                $"Expected .NET Framework 4.8 (PS 5.1) request {created.Id} to reach Completed, but got '{finalState.Status}'. Log: {combinedLog}");
            StringAssert.Contains(
                combinedLog,
                "VERIFIED_NETFX_48_POWERSHELL_51_RUNNER",
                $"Expected Dorc.NetFramework.Runner to execute Windows PowerShell 5.1 Desktop edition. Log: {combinedLog}");
        }

        [TestMethod]
        public void CreateDeploymentRequest_MultiComponentWithStopOnFailure_HaltsDownstreamComponent()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);
            var shouldFailProp = "AutoReg_ShouldFail";

            try
            {
                EnsurePropertyDefinition(caller, shouldFailProp, secure: false);
                UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "true");

                var requestPayload = new RequestDto
                {
                    Project = LiveProjectName,
                    Environment = LiveChildEnvName,
                    BuildNum = BuildFolderName,
                    BuildUrl = buildUri,
                    Components = new List<string>
                    {
                        FailThenPassComponentName,
                        DownstreamSkippedComponentName
                    }
                };

                var createResp = caller.ExecuteRaw("Request", Method.Post, body: requestPayload);
                Assert.AreEqual(HttpStatusCode.OK, createResp.StatusCode, $"POST /Request failed: {createResp.Content}");
                var created = DorcApiClient.Deserialize<RequestStatusDto>(createResp.Content);
                Assert.IsNotNull(created);
                Assert.IsGreaterThan(0, created.Id);

                var finalState = WaitForTerminalRequestStatus(caller, created.Id, TimeSpan.FromMinutes(3));
                Assert.IsTrue(
                    string.Equals(finalState.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(finalState.Status, "Errored", StringComparison.OrdinalIgnoreCase),
                    $"Expected multi-component request {created.Id} to fail when upstream StopOnFailure component fails, but got '{finalState.Status}'.");

                var resultsResp = caller.ExecuteRaw("ResultStatuses", Method.Get, new Dictionary<string, string>
                {
                    ["requestId"] = created.Id.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, resultsResp.StatusCode, resultsResp.Content);
                var results = DorcApiClient.Deserialize<List<DeploymentResultApiModel>>(resultsResp.Content) ?? new List<DeploymentResultApiModel>();
                var downstreamResult = results.FirstOrDefault(r =>
                    string.Equals(r.ComponentName, DownstreamSkippedComponentName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(downstreamResult, "Expected result record for downstream component.");
                Assert.IsTrue(
                    string.Equals(downstreamResult.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(downstreamResult.Status, "Pending", StringComparison.OrdinalIgnoreCase),
                    $"Downstream component should be 'Cancelled' or 'Pending' when upstream StopOnFailure=true component fails, but got '{downstreamResult.Status}'.");
                Assert.IsFalse(
                    (downstreamResult.Log ?? string.Empty).Contains("Downstream component executed.", StringComparison.Ordinal),
                    "Downstream component script must not execute when upstream StopOnFailure=true component fails.");
            }
            finally
            {
                UpsertPropertyValue(caller, shouldFailProp, secure: false, envFilter: LiveChildEnvName, value: "false");
            }
        }

        [TestMethod]
        public void CreateUpdateDeletePropertiesAndValues_MultiEnvironmentPropertyLifecycleAuditAndExport_PersistsAuditsExportsAndDeletesCleanly()
        {
            using var caller = new DorcApiClient();
            EnsureProvisioned(caller);

            var suffix = Guid.NewGuid().ToString("N")[..10];
            var runId = suffix;
            var plainPropName = $"DOrc_Live_Prop_{suffix}";
            var secretPropName = $"DOrc_Live_SecretProp_{suffix}";
            var csvImportPropName = $"DOrc_Live_CsvImport_{suffix}";
            var initialParentVal = $"InitialParent_{suffix}";
            var initialChildVal = $"InitialChild_{suffix}";
            var updatedParentVal = $"UpdatedParent_{suffix}";
            var updatedChildVal = $"UpdatedChild_{suffix}";
            var defaultVal = $"DefaultGlobal_{suffix}";
            var secretVal = $"TopSecret_{suffix}!";

            try
            {
                var createPropsResp = caller.ExecuteRaw("Properties", Method.Post, body: new object[]
                {
                    new { Name = plainPropName, Secure = false, IsArray = false },
                    new { Name = secretPropName, Secure = true, IsArray = false }
                });
                Assert.AreEqual(HttpStatusCode.OK, createPropsResp.StatusCode, $"POST /Properties failed: {createPropsResp.Content}");
                var createPropItems = DorcApiClient.Deserialize<List<Response>>(createPropsResp.Content) ?? new List<Response>();
                Assert.HasCount(2, createPropItems);
                Assert.IsTrue(createPropItems.All(r => string.Equals(r.Status, "success", StringComparison.OrdinalIgnoreCase)), createPropsResp.Content);

                var dupPropResp = caller.ExecuteRaw("Properties", Method.Post, body: new object[]
                {
                    new { Name = plainPropName, Secure = false, IsArray = false }
                });
                Assert.AreEqual(HttpStatusCode.OK, dupPropResp.StatusCode, dupPropResp.Content);
                var dupPropItems = DorcApiClient.Deserialize<List<Response>>(dupPropResp.Content) ?? new List<Response>();
                Assert.HasCount(1, dupPropItems);
                var allPropsAfterDupResp = caller.ExecuteRaw("Properties", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, allPropsAfterDupResp.StatusCode, allPropsAfterDupResp.Content);
                var allPropsAfterDup = DorcApiClient.Deserialize<List<PropertyApiModel>>(allPropsAfterDupResp.Content) ?? new List<PropertyApiModel>();
                Assert.AreEqual(
                    1,
                    allPropsAfterDup.Count(p => string.Equals(p.Name, plainPropName, StringComparison.OrdinalIgnoreCase)),
                    "Calling POST /Properties with an existing property name must be idempotent and never create duplicate property definitions.");

                var postValuesResp = caller.ExecuteRaw("PropertyValues", Method.Post, body: new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainPropName, Secure = false },
                        PropertyValueFilter = null,
                        DefaultValue = true,
                        Value = defaultVal
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainPropName, Secure = false },
                        PropertyValueFilter = LiveParentEnvName,
                        Value = initialParentVal
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainPropName, Secure = false },
                        PropertyValueFilter = LiveChildEnvName,
                        Value = initialChildVal
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = secretPropName, Secure = true },
                        PropertyValueFilter = LiveChildEnvName,
                        Value = secretVal
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, postValuesResp.StatusCode, $"POST /PropertyValues failed: {postValuesResp.Content}");
                var postValueItems = DorcApiClient.Deserialize<List<Response>>(postValuesResp.Content) ?? new List<Response>();
                Assert.HasCount(4, postValueItems);
                Assert.IsTrue(postValueItems.All(r => string.Equals(r.Status, "success", StringComparison.OrdinalIgnoreCase)), postValuesResp.Content);

                var dupValueResp = caller.ExecuteRaw("PropertyValues", Method.Post, body: new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainPropName, Secure = false },
                        PropertyValueFilter = LiveChildEnvName,
                        Value = "DuplicateAttempt"
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, dupValueResp.StatusCode, dupValueResp.Content);
                var dupValueItems = DorcApiClient.Deserialize<List<Response>>(dupValueResp.Content) ?? new List<Response>();
                Assert.HasCount(1, dupValueItems);
                Assert.AreNotEqual("success", dupValueItems[0].Status, "Creating a duplicate PropertyValue for the same environment must fail.");

                var putValuesResp = caller.ExecuteRaw("PropertyValues", Method.Put, body: new[]
                {
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainPropName, Secure = false },
                        PropertyValueFilter = LiveParentEnvName,
                        Value = updatedParentVal
                    },
                    new PropertyValueDto
                    {
                        Property = new PropertyApiModel { Name = plainPropName, Secure = false },
                        PropertyValueFilter = LiveChildEnvName,
                        Value = updatedChildVal
                    }
                });
                Assert.AreEqual(HttpStatusCode.OK, putValuesResp.StatusCode, $"PUT /PropertyValues failed: {putValuesResp.Content}");
                var putValueItems = DorcApiClient.Deserialize<List<Response>>(putValuesResp.Content) ?? new List<Response>();
                Assert.HasCount(2, putValueItems);
                Assert.IsTrue(putValueItems.All(r => string.Equals(r.Status, "success", StringComparison.OrdinalIgnoreCase)), putValuesResp.Content);

                var getByPropResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = plainPropName
                });
                Assert.AreEqual(HttpStatusCode.OK, getByPropResp.StatusCode, getByPropResp.Content);
                var propValues = DorcApiClient.Deserialize<List<PropertyValueDto>>(getByPropResp.Content) ?? new List<PropertyValueDto>();
                Assert.HasCount(3, propValues, "Expected 1 default value + 2 environment-scoped values.");

                var defaultDto = propValues.Single(pv => string.IsNullOrEmpty(pv.PropertyValueFilter));
                Assert.IsGreaterThan(0, defaultDto.Id);
                Assert.AreEqual(defaultVal, defaultDto.Value);
                Assert.IsTrue(string.IsNullOrEmpty(defaultDto.PropertyValueFilter));

                var parentDto = propValues.Single(pv => string.Equals(pv.PropertyValueFilter, LiveParentEnvName, StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(parentDto.DefaultValue);
                Assert.AreEqual(updatedParentVal, parentDto.Value);

                var childDto = propValues.Single(pv => string.Equals(pv.PropertyValueFilter, LiveChildEnvName, StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(childDto.DefaultValue);
                Assert.AreEqual(updatedChildVal, childDto.Value);

                var getByEnvResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["environmentName"] = LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, getByEnvResp.StatusCode, getByEnvResp.Content);
                var envValues = DorcApiClient.Deserialize<List<PropertyValueDto>>(getByEnvResp.Content) ?? new List<PropertyValueDto>();
                var secretDto = envValues.FirstOrDefault(pv => string.Equals(pv.Property?.Name, secretPropName, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(secretDto, "Expected secure property value in GET /PropertyValues?environmentName=...");
                Assert.IsTrue(secretDto.Property!.Secure);
                Assert.AreEqual(secretVal, secretDto.Value, "Authorized caller with secret read rights should receive decrypted secret value.");

                var csvMissingEnvResp = caller.ExecuteRaw("PropertyValues/non-secure-export", Method.Get);
                Assert.AreEqual(HttpStatusCode.BadRequest, csvMissingEnvResp.StatusCode);

                var csvExportResp = caller.ExecuteRaw("PropertyValues/non-secure-export", Method.Get, new Dictionary<string, string>
                {
                    ["environmentName"] = LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, csvExportResp.StatusCode, csvExportResp.Content);
                StringAssert.Contains(csvExportResp.Content ?? string.Empty, "PropertyName,Value,Is Secure");
                StringAssert.Contains(csvExportResp.Content ?? string.Empty, $"{plainPropName},{updatedChildVal},FALSE");
                Assert.IsFalse(
                    (csvExportResp.Content ?? string.Empty).Contains(secretPropName, StringComparison.OrdinalIgnoreCase),
                    "non-secure-export CSV must never include Secure=true properties.");
                Assert.IsFalse(
                    (csvExportResp.Content ?? string.Empty).Contains(secretVal, StringComparison.Ordinal),
                    "non-secure-export CSV must never include secret values.");

                var auditResp = caller.ExecuteRaw(
                    "PropertyValuesAudit",
                    Method.Put,
                    new Dictionary<string, string> { ["page"] = "1", ["limit"] = "50" },
                    body: new PagedDataOperators
                    {
                        Filters = new List<PagedDataFilter>
                        {
                            new PagedDataFilter { Path = "PropertyName", FilterValue = plainPropName }
                        }
                    });
                Assert.AreEqual(HttpStatusCode.OK, auditResp.StatusCode, auditResp.Content);
                StringAssert.Contains(auditResp.Content ?? string.Empty, initialChildVal);
                StringAssert.Contains(auditResp.Content ?? string.Empty, updatedChildVal);

                // ScopeOptions (Postman endpoint & 12-month commit e5429e32)
                var scopeOptionsResp = caller.ExecuteRaw("PropertyValues/ScopeOptions", Method.Get, new Dictionary<string, string>
                {
                    ["propertyValueScope"] = LiveChildEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, scopeOptionsResp.StatusCode, scopeOptionsResp.Content);
                var scopeOptions = DorcApiClient.Deserialize<List<PropertyValueScopeOptionApiModel>>(scopeOptionsResp.Content) ?? new List<PropertyValueScopeOptionApiModel>();
                Assert.IsNotEmpty(scopeOptions, "Expected non-empty ScopeOptions for environment.");
                Assert.IsTrue(scopeOptions.Any(o => string.Equals(o.ValueOption, "$EnvironmentName$", StringComparison.OrdinalIgnoreCase)),
                    "Expected ScopeOptions to contain $EnvironmentName$.");

                // POST /Properties/import CSV validation & import/update lifecycle (12-month commit 2046bf3d)
                var invalidImportBlankResp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = "", Value = "", IsSecure = false, Environment = LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, invalidImportBlankResp.StatusCode, invalidImportBlankResp.Content);
                StringAssert.Contains(invalidImportBlankResp.Content ?? string.Empty, "error: PropertyName cannot be blank or empty");
                StringAssert.Contains(invalidImportBlankResp.Content ?? string.Empty, "error: Property value cannot be blank or empty");

                var invalidImportConflictResp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = "Val1", IsSecure = false, Environment = "" },
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = "Val2", IsSecure = true, Environment = LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, invalidImportConflictResp.StatusCode, invalidImportConflictResp.Content);
                StringAssert.Contains(invalidImportConflictResp.Content ?? string.Empty, $"error: conflicting IsSecure values for property '{csvImportPropName}'");

                var importV1Resp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvDefault_V1_{runId}", IsSecure = false, Environment = "" },
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvChild_V1_{runId}", IsSecure = false, Environment = LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, importV1Resp.StatusCode, importV1Resp.Content);
                var importV1ValsResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = csvImportPropName
                });
                Assert.AreEqual(HttpStatusCode.OK, importV1ValsResp.StatusCode, importV1ValsResp.Content);
                var importedV1Vals = DorcApiClient.Deserialize<List<PropertyValueDto>>(importV1ValsResp.Content) ?? new List<PropertyValueDto>();
                Assert.HasCount(2, importedV1Vals);
                Assert.AreEqual($"CsvDefault_V1_{runId}", importedV1Vals.Single(v => string.IsNullOrEmpty(v.PropertyValueFilter)).Value);
                Assert.AreEqual($"CsvChild_V1_{runId}", importedV1Vals.Single(v => string.Equals(v.PropertyValueFilter, LiveChildEnvName, StringComparison.OrdinalIgnoreCase)).Value);

                var importV2Resp = caller.ExecuteRaw("Properties/import", Method.Post, body: new[]
                {
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvDefault_V2_{runId}", IsSecure = false, Environment = "" },
                    new PropertyImportCsvDto { PropertyName = csvImportPropName, Value = $"CsvChild_V2_{runId}", IsSecure = false, Environment = LiveChildEnvName }
                });
                Assert.AreEqual(HttpStatusCode.OK, importV2Resp.StatusCode, importV2Resp.Content);
                var importV2ValsResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = csvImportPropName
                });
                Assert.AreEqual(HttpStatusCode.OK, importV2ValsResp.StatusCode, importV2ValsResp.Content);
                var importedV2Vals = DorcApiClient.Deserialize<List<PropertyValueDto>>(importV2ValsResp.Content) ?? new List<PropertyValueDto>();
                Assert.AreEqual($"CsvDefault_V2_{runId}", importedV2Vals.Single(v => string.IsNullOrEmpty(v.PropertyValueFilter)).Value);
                Assert.AreEqual($"CsvChild_V2_{runId}", importedV2Vals.Single(v => string.Equals(v.PropertyValueFilter, LiveChildEnvName, StringComparison.OrdinalIgnoreCase)).Value);

                var deleteResp = caller.ExecuteRaw("Properties", Method.Delete, body: new[] { plainPropName, secretPropName, csvImportPropName });
                Assert.AreEqual(HttpStatusCode.OK, deleteResp.StatusCode, $"DELETE /Properties failed: {deleteResp.Content}");
                var deleteItems = DorcApiClient.Deserialize<List<Response>>(deleteResp.Content) ?? new List<Response>();
                Assert.HasCount(9, deleteItems, "Expected 6 deleted PropertyValue records + 3 deleted Property definitions.");
                Assert.IsTrue(deleteItems.All(r => string.Equals(r.Status, "success", StringComparison.OrdinalIgnoreCase)), deleteResp.Content);

                var allPropsResp = caller.ExecuteRaw("Properties", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, allPropsResp.StatusCode, allPropsResp.Content);
                var allProps = DorcApiClient.Deserialize<List<PropertyApiModel>>(allPropsResp.Content) ?? new List<PropertyApiModel>();
                Assert.IsFalse(allProps.Any(p => string.Equals(p.Name, plainPropName, StringComparison.OrdinalIgnoreCase)), "Deleted property must not appear in GET /Properties.");
                Assert.IsFalse(allProps.Any(p => string.Equals(p.Name, secretPropName, StringComparison.OrdinalIgnoreCase)), "Deleted secret property must not appear in GET /Properties.");
                Assert.IsFalse(allProps.Any(p => string.Equals(p.Name, csvImportPropName, StringComparison.OrdinalIgnoreCase)), "Deleted CSV import property must not appear in GET /Properties.");

                var deletedValuesResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = plainPropName
                });
                Assert.AreEqual(HttpStatusCode.NotFound, deletedValuesResp.StatusCode, "GET /PropertyValues for a deleted property must return 404 NotFound.");
            }
            finally
            {
                DeletePropertiesBestEffort(caller, plainPropName, secretPropName, csvImportPropName);
            }
        }

        [TestMethod]
        public void BuildServersAndInstalledCliTools_AdoAndGitHubBuildResolutionAndInstalledCliExecution_ResolvesArtifactsAndExecutesInstalledTools()
        {
            using var caller = new DorcApiClient();
            var buildUri = EnsureProvisioned(caller);

            var runId = Guid.NewGuid().ToString("N")[..8];
            var adoProjectName = $"DOrc_Live_AdoBuild_{runId}";
            var ghProjectName = $"DOrc_Live_GitHubBuild_{runId}";
            var cliPropName = $"DOrc_Live_CliCreated_{runId}";
            int? adoProjectId = null;
            int? ghProjectId = null;

            try
            {
                // 1. FileShare BuildDefinitions & Builds discovery on LiveProjectName
                var liveProjResp = caller.ExecuteRaw($"RefDataProjects/{Uri.EscapeDataString(LiveProjectName)}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, liveProjResp.StatusCode, liveProjResp.Content);
                var liveProj = DorcApiClient.Deserialize<ProjectApiModel>(liveProjResp.Content);
                Assert.IsNotNull(liveProj);

                var fsBuildDefsResp = caller.ExecuteRaw("Request/BuildDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = liveProj.ProjectId.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, fsBuildDefsResp.StatusCode, fsBuildDefsResp.Content);
                var fsBuildDefs = DorcApiClient.Deserialize<List<DeployArtefactDto>>(fsBuildDefsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsNotEmpty(fsBuildDefs, $"Expected FileShare project to return build definitions. Actual: {fsBuildDefsResp.Content}");
                var fsDefName = fsBuildDefs[0].Name;

                var fsBuildsResp = caller.ExecuteRaw("Request/Builds", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = liveProj.ProjectId.ToString(),
                    ["environment"] = LiveParentEnvName,
                    ["buildDefinitionName"] = fsDefName
                });
                Assert.AreEqual(HttpStatusCode.OK, fsBuildsResp.StatusCode, fsBuildsResp.Content);
                var fsBuilds = DorcApiClient.Deserialize<List<DeployArtefactDto>>(fsBuildsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsTrue(fsBuilds.Any(b => b.Name != null && b.Name.Contains(BuildFolderName, StringComparison.OrdinalIgnoreCase)),
                    $"Expected FileShare project builds to include '{BuildFolderName}'. Actual: {fsBuildsResp.Content}");

                // 2. Azure DevOps BuildDefinitions, Builds, and live deployment + installed CLI tools execution on DEPAPP01DV
                EnsurePropertyDefinition(caller, "AutoReg_CliPropName", secure: false);
                UpsertPropertyValue(caller, "AutoReg_CliPropName", secure: false, envFilter: LiveParentEnvName, value: cliPropName);
                EnsurePropertyDefinition(caller, "AutoReg_FileShareBuildUri", secure: false);
                UpsertPropertyValue(caller, "AutoReg_FileShareBuildUri", secure: false, envFilter: LiveParentEnvName, value: buildUri);

                var createAdoProjResp = caller.ExecuteRaw("RefDataProjects", Method.Post, body: new ProjectApiModel
                {
                    ProjectName = adoProjectName,
                    ProjectDescription = $"Live ADO build & CLI verification {runId}",
                    SourceControlType = SourceControlType.AzureDevOps,
                    ArtefactsUrl = "https://dev.azure.com/sefe/",
                    ArtefactsSubPaths = "Deployment Orchestrator",
                    ArtefactsBuildRegex = "dorc-active-batch"
                });
                Assert.AreEqual(HttpStatusCode.OK, createAdoProjResp.StatusCode, createAdoProjResp.Content);
                var adoCreatedProj = DorcApiClient.Deserialize<ProjectApiModel>(createAdoProjResp.Content);
                Assert.IsNotNull(adoCreatedProj);
                adoProjectId = adoCreatedProj.ProjectId;

                var mapAdoEnvResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Post, new Dictionary<string, string>
                {
                    ["project"] = adoProjectName,
                    ["environment"] = LiveParentEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, mapAdoEnvResp.StatusCode, mapAdoEnvResp.Content);

                var adoCompName = $"Live_AdoBuild_Comp_{runId}";
                var getAdoRefResp = caller.ExecuteRaw($"RefData/{adoProjectId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getAdoRefResp.StatusCode, getAdoRefResp.Content);
                var adoTemplate = DorcApiClient.Deserialize<RefDataApiModel>(getAdoRefResp.Content);
                Assert.IsNotNull(adoTemplate);
                adoTemplate.Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 0,
                        ComponentName = adoCompName,
                        ScriptPath = Path.Join(RelativeScriptSubfolder, "verify-ado-build-and-cli.ps1"),
                        NonProdOnly = true,
                        StopOnFailure = true,
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        PSVersion = PowerShellVersion.V7.ToVersionString(),
                        Children = new List<ComponentApiModel>()
                    }
                };
                var putAdoRefResp = caller.ExecuteRaw("RefData", Method.Put, body: adoTemplate);
                Assert.AreEqual(HttpStatusCode.OK, putAdoRefResp.StatusCode, putAdoRefResp.Content);

                var adoBuildDefsResp = caller.ExecuteRaw("Request/BuildDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = adoProjectId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, adoBuildDefsResp.StatusCode, adoBuildDefsResp.Content);
                var adoBuildDefs = DorcApiClient.Deserialize<List<DeployArtefactDto>>(adoBuildDefsResp.Content) ?? new List<DeployArtefactDto>();
                var adoDef = adoBuildDefs.FirstOrDefault(d => d.Name != null && d.Name.Contains("dorc-active-batch", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(adoDef, $"Expected ADO BuildDefinitions to include 'dorc-active-batch'. Actual: {adoBuildDefsResp.Content}");

                var adoBuildsResp = caller.ExecuteRaw("Request/Builds", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = adoProjectId.Value.ToString(),
                    ["environment"] = LiveParentEnvName,
                    ["buildDefinitionName"] = adoDef.Name
                });
                Assert.AreEqual(HttpStatusCode.OK, adoBuildsResp.StatusCode, adoBuildsResp.Content);
                var adoBuilds = DorcApiClient.Deserialize<List<DeployArtefactDto>>(adoBuildsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsNotEmpty(adoBuilds, "Expected non-empty builds list from Azure DevOps build server.");

                var adoRequestResp = caller.ExecuteRaw("Request", Method.Post, body: new RequestDto
                {
                    Project = adoProjectName,
                    Environment = LiveParentEnvName,
                    BuildText = adoDef.Name,
                    BuildNum = "latest",
                    Components = new List<string> { adoCompName }
                });
                Assert.AreEqual(HttpStatusCode.OK, adoRequestResp.StatusCode, $"ADO Request submission failed: {adoRequestResp.Content}");
                var adoCreatedReq = DorcApiClient.Deserialize<RequestStatusDto>(adoRequestResp.Content);
                Assert.IsNotNull(adoCreatedReq);

                var adoFinalStatus = WaitForTerminalRequestStatus(caller, adoCreatedReq.Id, TimeSpan.FromMinutes(3));
                var adoJobLog = GetCombinedRequestLog(caller, adoCreatedReq.Id);
                Assert.AreEqual("Completed", adoFinalStatus.Status, $"Expected ADO build deployment #{adoCreatedReq.Id} to complete. Log:\n{adoJobLog}");

                StringAssert.Contains(adoJobLog, $"ADO_DEPLOY_VERIFIED|ENV={LiveParentEnvName}|BUILD=dorc-activebatch.");
                StringAssert.Contains(adoJobLog, "DROP=");
                StringAssert.Contains(adoJobLog, "PROP_CLI_OUT=");
                StringAssert.Contains(adoJobLog, "REQ_CLI_OUT=");

                // Wait for the CLI-triggered request to finish so it doesn't overlap subsequent checks
                var cliReqMatch = System.Text.RegularExpressions.Regex.Match(adoJobLog, @"Request (\d+) created");
                Assert.IsTrue(cliReqMatch.Success, $"Expected RequestCLI output with request ID in log: {adoJobLog}");
                var cliTriggeredRequestId = int.Parse(cliReqMatch.Groups[1].Value);
                var cliFinalStatus = WaitForTerminalRequestStatus(caller, cliTriggeredRequestId, TimeSpan.FromMinutes(3));
                Assert.AreEqual("Completed", cliFinalStatus.Status, $"Expected CLI-triggered request #{cliTriggeredRequestId} to complete.");

                // Verify PropertyValueCreationCLI created the property and value in DOrc DV 01
                var cliPropValsResp = caller.ExecuteRaw("PropertyValues", Method.Get, new Dictionary<string, string>
                {
                    ["propertyName"] = cliPropName
                });
                Assert.AreEqual(HttpStatusCode.OK, cliPropValsResp.StatusCode, cliPropValsResp.Content);
                var cliPropVals = DorcApiClient.Deserialize<List<PropertyValueDto>>(cliPropValsResp.Content) ?? new List<PropertyValueDto>();
                Assert.IsTrue(cliPropVals.Any(pv =>
                    string.Equals(pv.PropertyValueFilter, LiveParentEnvName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pv.Value, $"CliCreatedValue_{cliPropName}", StringComparison.Ordinal)),
                    $"Expected Tools.PropertyValueCreationCLI.exe to create '{cliPropName}' on '{LiveParentEnvName}'.");

                // Verify RefDataProjectBuilds records the deployed ADO build on LiveParentEnvName
                var projBuildsResp = caller.ExecuteRaw("RefDataProjectBuilds", Method.Get, new Dictionary<string, string>
                {
                    ["id"] = LiveParentEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, projBuildsResp.StatusCode, projBuildsResp.Content);
                StringAssert.Contains(projBuildsResp.Content ?? string.Empty, "dorc-activebatch.");

                // 3. GitHub Actions BuildDefinitions, Builds, and live GitHubArtifactDownloader zip download/extraction deployment
                var createGhProjResp = caller.ExecuteRaw("RefDataProjects", Method.Post, body: new ProjectApiModel
                {
                    ProjectName = ghProjectName,
                    ProjectDescription = $"Live GitHub Actions artifact verification {runId}",
                    SourceControlType = SourceControlType.GitHub,
                    ArtefactsUrl = "https://api.github.com/repos/sefe/dorc-ps-deploy-module",
                    ArtefactsSubPaths = "build.yml",
                    ArtefactsBuildRegex = ".*"
                });
                Assert.AreEqual(HttpStatusCode.OK, createGhProjResp.StatusCode, createGhProjResp.Content);
                var ghCreatedProj = DorcApiClient.Deserialize<ProjectApiModel>(createGhProjResp.Content);
                Assert.IsNotNull(ghCreatedProj);
                ghProjectId = ghCreatedProj.ProjectId;

                var mapGhEnvResp = caller.ExecuteRaw("RefDataProjectEnvironmentMappings", Method.Post, new Dictionary<string, string>
                {
                    ["project"] = ghProjectName,
                    ["environment"] = LiveParentEnvName
                });
                Assert.AreEqual(HttpStatusCode.OK, mapGhEnvResp.StatusCode, mapGhEnvResp.Content);

                var ghCompName = $"Live_GitHubBuild_Comp_{runId}";
                var getGhRefResp = caller.ExecuteRaw($"RefData/{ghProjectId.Value}", Method.Get);
                Assert.AreEqual(HttpStatusCode.OK, getGhRefResp.StatusCode, getGhRefResp.Content);
                var ghTemplate = DorcApiClient.Deserialize<RefDataApiModel>(getGhRefResp.Content);
                Assert.IsNotNull(ghTemplate);
                ghTemplate.Components = new List<ComponentApiModel>
                {
                    new ComponentApiModel
                    {
                        ComponentId = 0,
                        ComponentName = ghCompName,
                        ScriptPath = Path.Join(RelativeScriptSubfolder, "verify-github-build.ps1"),
                        NonProdOnly = true,
                        StopOnFailure = true,
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        PSVersion = PowerShellVersion.V7.ToVersionString(),
                        Children = new List<ComponentApiModel>()
                    }
                };
                var putGhRefResp = caller.ExecuteRaw("RefData", Method.Put, body: ghTemplate);
                Assert.AreEqual(HttpStatusCode.OK, putGhRefResp.StatusCode, putGhRefResp.Content);

                var ghBuildDefsResp = caller.ExecuteRaw("Request/BuildDefinitions", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = ghProjectId.Value.ToString()
                });
                Assert.AreEqual(HttpStatusCode.OK, ghBuildDefsResp.StatusCode, ghBuildDefsResp.Content);
                var ghBuildDefs = DorcApiClient.Deserialize<List<DeployArtefactDto>>(ghBuildDefsResp.Content) ?? new List<DeployArtefactDto>();
                var ghDef = ghBuildDefs.FirstOrDefault(d => string.Equals(d.Name, "Build DOrcDeployModule", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(ghDef, $"Expected GitHub BuildDefinitions to include 'Build DOrcDeployModule'. Actual: {ghBuildDefsResp.Content}");

                var ghBuildsResp = caller.ExecuteRaw("Request/Builds", Method.Get, new Dictionary<string, string>
                {
                    ["projectId"] = ghProjectId.Value.ToString(),
                    ["environment"] = LiveParentEnvName,
                    ["buildDefinitionName"] = ghDef.Name
                });
                Assert.AreEqual(HttpStatusCode.OK, ghBuildsResp.StatusCode, ghBuildsResp.Content);
                var ghBuilds = DorcApiClient.Deserialize<List<DeployArtefactDto>>(ghBuildsResp.Content) ?? new List<DeployArtefactDto>();
                Assert.IsNotEmpty(ghBuilds, "Expected non-empty workflow runs from GitHub Actions build server.");

                var ghRequestResp = caller.ExecuteRaw("Request", Method.Post, body: new RequestDto
                {
                    Project = ghProjectName,
                    Environment = LiveParentEnvName,
                    BuildText = ghDef.Name,
                    BuildNum = "latest",
                    Components = new List<string> { ghCompName }
                });
                Assert.AreEqual(HttpStatusCode.OK, ghRequestResp.StatusCode, $"GitHub Request submission failed: {ghRequestResp.Content}");
                var ghCreatedReq = DorcApiClient.Deserialize<RequestStatusDto>(ghRequestResp.Content);
                Assert.IsNotNull(ghCreatedReq);

                var ghFinalStatus = WaitForTerminalRequestStatus(caller, ghCreatedReq.Id, TimeSpan.FromMinutes(3));
                var ghJobLog = GetCombinedRequestLog(caller, ghCreatedReq.Id);
                Assert.AreEqual("Completed", ghFinalStatus.Status, $"Expected GitHub artifact deployment #{ghCreatedReq.Id} to complete. Log:\n{ghJobLog}");

                StringAssert.Contains(ghJobLog, $"GITHUB_DEPLOY_VERIFIED|ENV={LiveParentEnvName}|BUILD=");
                StringAssert.Contains(ghJobLog, "FILE_COUNT=");
            }
            finally
            {
                DeletePropertiesBestEffort(caller, cliPropName, "AutoReg_FileShareBuildUri");
                if (adoProjectId.HasValue)
                {
                    caller.ExecuteRaw("RefDataProjects", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = adoProjectId.Value.ToString()
                    });
                }
                if (ghProjectId.HasValue)
                {
                    caller.ExecuteRaw("RefDataProjects", Method.Delete, new Dictionary<string, string>
                    {
                        ["id"] = ghProjectId.Value.ToString()
                    });
                }
            }
        }
    }
}
