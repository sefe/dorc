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
    /// Contains the settings for the retention rules.
    /// </summary>
    [DataContract(Name = "ProjectRetentionSetting")]
    public partial class ProjectRetentionSetting : IEquatable<ProjectRetentionSetting>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectRetentionSetting" /> class.
        /// </summary>
        /// <param name="purgeArtifacts">purgeArtifacts.</param>
        /// <param name="purgePullRequestRuns">purgePullRequestRuns.</param>
        /// <param name="purgeRuns">purgeRuns.</param>
        /// <param name="retainRunsPerProtectedBranch">retainRunsPerProtectedBranch.</param>
        public ProjectRetentionSetting(RetentionSetting purgeArtifacts = default(RetentionSetting), RetentionSetting purgePullRequestRuns = default(RetentionSetting), RetentionSetting purgeRuns = default(RetentionSetting), RetentionSetting retainRunsPerProtectedBranch = default(RetentionSetting))
        {
            this.PurgeArtifacts = purgeArtifacts;
            this.PurgePullRequestRuns = purgePullRequestRuns;
            this.PurgeRuns = purgeRuns;
            this.RetainRunsPerProtectedBranch = retainRunsPerProtectedBranch;
        }

        /// <summary>
        /// Gets or Sets PurgeArtifacts
        /// </summary>
        [DataMember(Name = "purgeArtifacts", EmitDefaultValue = false)]
        public RetentionSetting PurgeArtifacts { get; set; }

        /// <summary>
        /// Gets or Sets PurgePullRequestRuns
        /// </summary>
        [DataMember(Name = "purgePullRequestRuns", EmitDefaultValue = false)]
        public RetentionSetting PurgePullRequestRuns { get; set; }

        /// <summary>
        /// Gets or Sets PurgeRuns
        /// </summary>
        [DataMember(Name = "purgeRuns", EmitDefaultValue = false)]
        public RetentionSetting PurgeRuns { get; set; }

        /// <summary>
        /// Gets or Sets RetainRunsPerProtectedBranch
        /// </summary>
        [DataMember(Name = "retainRunsPerProtectedBranch", EmitDefaultValue = false)]
        public RetentionSetting RetainRunsPerProtectedBranch { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ProjectRetentionSetting {\n");
            sb.Append("  PurgeArtifacts: ").Append(PurgeArtifacts).Append("\n");
            sb.Append("  PurgePullRequestRuns: ").Append(PurgePullRequestRuns).Append("\n");
            sb.Append("  PurgeRuns: ").Append(PurgeRuns).Append("\n");
            sb.Append("  RetainRunsPerProtectedBranch: ").Append(RetainRunsPerProtectedBranch).Append("\n");
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
            return this.Equals(input as ProjectRetentionSetting);
        }

        /// <summary>
        /// Returns true if ProjectRetentionSetting instances are equal
        /// </summary>
        /// <param name="input">Instance of ProjectRetentionSetting to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(ProjectRetentionSetting input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.PurgeArtifacts == input.PurgeArtifacts ||
                    (this.PurgeArtifacts != null &&
                    this.PurgeArtifacts.Equals(input.PurgeArtifacts))
                ) && 
                (
                    this.PurgePullRequestRuns == input.PurgePullRequestRuns ||
                    (this.PurgePullRequestRuns != null &&
                    this.PurgePullRequestRuns.Equals(input.PurgePullRequestRuns))
                ) && 
                (
                    this.PurgeRuns == input.PurgeRuns ||
                    (this.PurgeRuns != null &&
                    this.PurgeRuns.Equals(input.PurgeRuns))
                ) && 
                (
                    this.RetainRunsPerProtectedBranch == input.RetainRunsPerProtectedBranch ||
                    (this.RetainRunsPerProtectedBranch != null &&
                    this.RetainRunsPerProtectedBranch.Equals(input.RetainRunsPerProtectedBranch))
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
                if (this.PurgeArtifacts != null)
                {
                    hashCode = (hashCode * 59) + this.PurgeArtifacts.GetHashCode();
                }
                if (this.PurgePullRequestRuns != null)
                {
                    hashCode = (hashCode * 59) + this.PurgePullRequestRuns.GetHashCode();
                }
                if (this.PurgeRuns != null)
                {
                    hashCode = (hashCode * 59) + this.PurgeRuns.GetHashCode();
                }
                if (this.RetainRunsPerProtectedBranch != null)
                {
                    hashCode = (hashCode * 59) + this.RetainRunsPerProtectedBranch.GetHashCode();
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
