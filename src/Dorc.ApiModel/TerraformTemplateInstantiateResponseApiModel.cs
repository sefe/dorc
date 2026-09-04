using System.Text.Json.Serialization;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Response body for POST /Terraform/templates/{name}/{version}/instantiate.
    /// Always carries the created (or reused) Catalog-mode <see cref="Component"/>.
    /// In create-and-deploy mode (an EnvironmentName was supplied) it additionally
    /// carries the submitted deploy <see cref="RequestId"/> and <see cref="RequestStatus"/>;
    /// in create-component-only mode those two are left at their defaults.
    ///
    /// The JSON member names are deliberately lower-camel (component / requestId /
    /// requestStatus) to preserve the wire shape of the anonymous envelope this
    /// endpoint previously returned, which the dorc-web client already consumes.
    /// </summary>
    public class TerraformTemplateInstantiateResponseApiModel
    {
        /// <summary>The Catalog-mode component that was created (or reused on retry).</summary>
        [JsonPropertyName("component")]
        public ComponentApiModel Component { get; set; }

        /// <summary>
        /// Id of the deploy request submitted for the new component in
        /// create-and-deploy mode. Left at 0 in create-component-only mode.
        /// </summary>
        [JsonPropertyName("requestId")]
        public int RequestId { get; set; }

        /// <summary>
        /// Status text of the submitted deploy request in create-and-deploy
        /// mode. Null in create-component-only mode.
        /// </summary>
        [JsonPropertyName("requestStatus")]
        public string RequestStatus { get; set; }
    }
}
