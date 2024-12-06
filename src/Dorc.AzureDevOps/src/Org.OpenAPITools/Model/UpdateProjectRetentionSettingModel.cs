/*
 * Build
 *
 * No description provided (generated by Openapi Generator https://github.com/openapitools/openapi-generator)
 *
 * The version of the OpenAPI document: 6.0
 * Contact: nugetvss@microsoft.com
 * Generated by: https://github.com/openapitools/openapi-generator.git
 */


using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using OpenAPIDateConverter = Org.OpenAPITools.Client.OpenAPIDateConverter;

namespace Org.OpenAPITools.Model
{
    /// <summary>
    /// Contains members for updating the retention settings values. All fields are optional.
    /// </summary>
    [DataContract(Name = "UpdateProjectRetentionSettingModel")]
    public partial class UpdateProjectRetentionSettingModel : IEquatable<UpdateProjectRetentionSettingModel>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateProjectRetentionSettingModel" /> class.
        /// </summary>
        /// <param name="artifactsRetention">artifactsRetention.</param>
        /// <param name="pullRequestRunRetention">pullRequestRunRetention.</param>
        /// <param name="retainRunsPerProtectedBranch">retainRunsPerProtectedBranch.</param>
        /// <param name="runRetention">runRetention.</param>
        public UpdateProjectRetentionSettingModel(UpdateRetentionSettingModel artifactsRetention = default(UpdateRetentionSettingModel), UpdateRetentionSettingModel pullRequestRunRetention = default(UpdateRetentionSettingModel), UpdateRetentionSettingModel retainRunsPerProtectedBranch = default(UpdateRetentionSettingModel), UpdateRetentionSettingModel runRetention = default(UpdateRetentionSettingModel))
        {
            this.ArtifactsRetention = artifactsRetention;
            this.PullRequestRunRetention = pullRequestRunRetention;
            this.RetainRunsPerProtectedBranch = retainRunsPerProtectedBranch;
            this.RunRetention = runRetention;
        }

        /// <summary>
        /// Gets or Sets ArtifactsRetention
        /// </summary>
        [DataMember(Name = "artifactsRetention", EmitDefaultValue = false)]
        public UpdateRetentionSettingModel ArtifactsRetention { get; set; }

        /// <summary>
        /// Gets or Sets PullRequestRunRetention
        /// </summary>
        [DataMember(Name = "pullRequestRunRetention", EmitDefaultValue = false)]
        public UpdateRetentionSettingModel PullRequestRunRetention { get; set; }

        /// <summary>
        /// Gets or Sets RetainRunsPerProtectedBranch
        /// </summary>
        [DataMember(Name = "retainRunsPerProtectedBranch", EmitDefaultValue = false)]
        public UpdateRetentionSettingModel RetainRunsPerProtectedBranch { get; set; }

        /// <summary>
        /// Gets or Sets RunRetention
        /// </summary>
        [DataMember(Name = "runRetention", EmitDefaultValue = false)]
        public UpdateRetentionSettingModel RunRetention { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class UpdateProjectRetentionSettingModel {\n");
            sb.Append("  ArtifactsRetention: ").Append(ArtifactsRetention).Append("\n");
            sb.Append("  PullRequestRunRetention: ").Append(PullRequestRunRetention).Append("\n");
            sb.Append("  RetainRunsPerProtectedBranch: ").Append(RetainRunsPerProtectedBranch).Append("\n");
            sb.Append("  RunRetention: ").Append(RunRetention).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public virtual string ToJson()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Returns true if objects are equal
        /// </summary>
        /// <param name="input">Object to be compared</param>
        /// <returns>Boolean</returns>
        public override bool Equals(object input)
        {
            return this.Equals(input as UpdateProjectRetentionSettingModel);
        }

        /// <summary>
        /// Returns true if UpdateProjectRetentionSettingModel instances are equal
        /// </summary>
        /// <param name="input">Instance of UpdateProjectRetentionSettingModel to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(UpdateProjectRetentionSettingModel input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.ArtifactsRetention == input.ArtifactsRetention ||
                    (this.ArtifactsRetention != null &&
                    this.ArtifactsRetention.Equals(input.ArtifactsRetention))
                ) && 
                (
                    this.PullRequestRunRetention == input.PullRequestRunRetention ||
                    (this.PullRequestRunRetention != null &&
                    this.PullRequestRunRetention.Equals(input.PullRequestRunRetention))
                ) && 
                (
                    this.RetainRunsPerProtectedBranch == input.RetainRunsPerProtectedBranch ||
                    (this.RetainRunsPerProtectedBranch != null &&
                    this.RetainRunsPerProtectedBranch.Equals(input.RetainRunsPerProtectedBranch))
                ) && 
                (
                    this.RunRetention == input.RunRetention ||
                    (this.RunRetention != null &&
                    this.RunRetention.Equals(input.RunRetention))
                );
        }

        /// <summary>
        /// Gets the hash code
        /// </summary>
        /// <returns>Hash code</returns>
        public override int GetHashCode()
        {
            unchecked // Overflow is fine, just wrap
            {
                int hashCode = 41;
                if (this.ArtifactsRetention != null)
                {
                    hashCode = (hashCode * 59) + this.ArtifactsRetention.GetHashCode();
                }
                if (this.PullRequestRunRetention != null)
                {
                    hashCode = (hashCode * 59) + this.PullRequestRunRetention.GetHashCode();
                }
                if (this.RetainRunsPerProtectedBranch != null)
                {
                    hashCode = (hashCode * 59) + this.RetainRunsPerProtectedBranch.GetHashCode();
                }
                if (this.RunRetention != null)
                {
                    hashCode = (hashCode * 59) + this.RunRetention.GetHashCode();
                }
                return hashCode;
            }
        }

        /// <summary>
        /// To validate all properties of the instance
        /// </summary>
        /// <param name="validationContext">Validation context</param>
        /// <returns>Validation Result</returns>
        public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(ValidationContext validationContext)
        {
            yield break;
        }
    }

}
