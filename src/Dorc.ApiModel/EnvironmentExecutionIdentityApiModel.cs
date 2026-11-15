using System.Text.Json.Serialization;

namespace Dorc.ApiModel
{
    public class EnvironmentExecutionIdentityApiModel
    {
        private string _executionIdentityReference;

        /// <summary>
        /// The named deployment identity to bind, or null/empty to restore the tier default.
        /// </summary>
        public string ExecutionIdentityReference
        {
            get => _executionIdentityReference;
            set
            {
                _executionIdentityReference = value;
                IsExecutionIdentityReferenceSpecified = true;
            }
        }

        [JsonIgnore]
        public bool IsExecutionIdentityReferenceSpecified { get; private set; }
    }
}
