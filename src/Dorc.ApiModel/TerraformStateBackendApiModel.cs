using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dorc.ApiModel
{
    public class TerraformStateBackendApiModel
    {
        public int? EnvironmentId { get; set; }
        public string StorageAccount { get; set; }
        public string ContainerName { get; set; }
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
    }

    public static class TerraformStateBackends
    {
        public static void Validate(IEnumerable<TerraformStateBackendApiModel> backends)
        {
            var scopes = new HashSet<int?>();
            foreach (var backend in backends ?? Enumerable.Empty<TerraformStateBackendApiModel>())
            {
                if (backend == null || backend.EnvironmentId <= 0 || !scopes.Add(backend.EnvironmentId))
                    throw new ArgumentException("Configure at most one default Terraform backend and one override per environment.");
                if (!Regex.IsMatch(backend.StorageAccount ?? "", @"\A[a-z0-9]{3,24}\z") ||
                    !Regex.IsMatch(backend.ContainerName ?? "", @"\A[a-z0-9](?:[a-z0-9]|-(?!-)){1,61}[a-z0-9]\z") ||
                    !Guid.TryParse(backend.SubscriptionId, out var subscription) || subscription == Guid.Empty ||
                    (!string.IsNullOrEmpty(backend.ResourceGroup) &&
                     !Regex.IsMatch(backend.ResourceGroup, @"\A[a-zA-Z0-9_().-]{0,89}[a-zA-Z0-9_()-]\z")))
                    throw new ArgumentException("Terraform backend requires a valid Azure storage account, container, subscription GUID and optional resource-group name. Do not supply URLs or credentials.");
            }
        }

        public static TerraformStateBackendApiModel Resolve(IEnumerable<TerraformStateBackendApiModel> backends, int environmentId)
        {
            var values = (backends ?? Enumerable.Empty<TerraformStateBackendApiModel>()).ToList();
            Validate(values);
            return values.SingleOrDefault(value => value.EnvironmentId == environmentId)
                ?? values.SingleOrDefault(value => !value.EnvironmentId.HasValue);
        }

        public static TerraformStateBackendApiModel Require(IEnumerable<TerraformStateBackendApiModel> backends, int environmentId)
        {
            return Resolve(backends, environmentId) ?? throw new InvalidOperationException(
                "Configure a Terraform state backend for this project/environment in Project Ref Data before submitting a Catalog deployment. Monitor-wide settings are not used.");
        }

        public static TerraformStateBackendApiModel RequireSubmitted(IEnumerable<TerraformStateBackendApiModel> backends)
        {
            var values = (backends ?? Enumerable.Empty<TerraformStateBackendApiModel>()).ToList();
            Validate(values);
            if (values.Count != 1 || !values[0].EnvironmentId.HasValue)
                throw new InvalidOperationException("This request has no pinned project/environment state backend. Resubmit it and review a new plan; live backend fallback is disabled.");
            return values[0];
        }

        public static bool SameLocation(TerraformStateBackendApiModel left, TerraformStateBackendApiModel right)
        {
            if (left == null || right == null) return left == right;
            return left.StorageAccount == right.StorageAccount && left.ContainerName == right.ContainerName &&
                Guid.Parse(left.SubscriptionId) == Guid.Parse(right.SubscriptionId) &&
                string.Equals(left.ResourceGroup ?? "", right.ResourceGroup ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }
}
