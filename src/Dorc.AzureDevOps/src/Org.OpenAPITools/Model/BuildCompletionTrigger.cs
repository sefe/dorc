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
    /// Represents a build completion trigger.
    /// </summary>
    [DataContract(Name = "BuildCompletionTrigger")]
    public partial class BuildCompletionTrigger : IEquatable<BuildCompletionTrigger>, IValidatableObject
    {
        /// <summary>
        /// The type of the trigger.
        /// </summary>
        /// <value>The type of the trigger.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum TriggerTypeEnum
        {
            /// <summary>
            /// Enum None for value: none
            /// </summary>
            [EnumMember(Value = "none")]
            None = 1,

            /// <summary>
            /// Enum ContinuousIntegration for value: continuousIntegration
            /// </summary>
            [EnumMember(Value = "continuousIntegration")]
            ContinuousIntegration = 2,

            /// <summary>
            /// Enum BatchedContinuousIntegration for value: batchedContinuousIntegration
            /// </summary>
            [EnumMember(Value = "batchedContinuousIntegration")]
            BatchedContinuousIntegration = 3,

            /// <summary>
            /// Enum Schedule for value: schedule
            /// </summary>
            [EnumMember(Value = "schedule")]
            Schedule = 4,

            /// <summary>
            /// Enum GatedCheckIn for value: gatedCheckIn
            /// </summary>
            [EnumMember(Value = "gatedCheckIn")]
            GatedCheckIn = 5,

            /// <summary>
            /// Enum BatchedGatedCheckIn for value: batchedGatedCheckIn
            /// </summary>
            [EnumMember(Value = "batchedGatedCheckIn")]
            BatchedGatedCheckIn = 6,

            /// <summary>
            /// Enum PullRequest for value: pullRequest
            /// </summary>
            [EnumMember(Value = "pullRequest")]
            PullRequest = 7,

            /// <summary>
            /// Enum BuildCompletion for value: buildCompletion
            /// </summary>
            [EnumMember(Value = "buildCompletion")]
            BuildCompletion = 8,

            /// <summary>
            /// Enum All for value: all
            /// </summary>
            [EnumMember(Value = "all")]
            All = 9

        }


        /// <summary>
        /// The type of the trigger.
        /// </summary>
        /// <value>The type of the trigger.</value>
        [DataMember(Name = "triggerType", EmitDefaultValue = false)]
        public TriggerTypeEnum? TriggerType { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCompletionTrigger" /> class.
        /// </summary>
        /// <param name="branchFilters">branchFilters.</param>
        /// <param name="definition">definition.</param>
        /// <param name="requiresSuccessfulBuild">requiresSuccessfulBuild.</param>
        /// <param name="triggerType">The type of the trigger..</param>
        public BuildCompletionTrigger(List<string> branchFilters = default(List<string>), DefinitionReference definition = default(DefinitionReference), bool requiresSuccessfulBuild = default(bool), TriggerTypeEnum? triggerType = default(TriggerTypeEnum?))
        {
            this.BranchFilters = branchFilters;
            this.Definition = definition;
            this.RequiresSuccessfulBuild = requiresSuccessfulBuild;
            this.TriggerType = triggerType;
        }

        /// <summary>
        /// Gets or Sets BranchFilters
        /// </summary>
        [DataMember(Name = "branchFilters", EmitDefaultValue = false)]
        public List<string> BranchFilters { get; set; }

        /// <summary>
        /// Gets or Sets Definition
        /// </summary>
        [DataMember(Name = "definition", EmitDefaultValue = false)]
        public DefinitionReference Definition { get; set; }

        /// <summary>
        /// Gets or Sets RequiresSuccessfulBuild
        /// </summary>
        [DataMember(Name = "requiresSuccessfulBuild", EmitDefaultValue = true)]
        public bool RequiresSuccessfulBuild { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildCompletionTrigger {\n");
            sb.Append("  BranchFilters: ").Append(BranchFilters).Append("\n");
            sb.Append("  Definition: ").Append(Definition).Append("\n");
            sb.Append("  RequiresSuccessfulBuild: ").Append(RequiresSuccessfulBuild).Append("\n");
            sb.Append("  TriggerType: ").Append(TriggerType).Append("\n");
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
            return this.Equals(input as BuildCompletionTrigger);
        }

        /// <summary>
        /// Returns true if BuildCompletionTrigger instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildCompletionTrigger to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildCompletionTrigger input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.BranchFilters == input.BranchFilters ||
                    this.BranchFilters != null &&
                    input.BranchFilters != null &&
                    this.BranchFilters.SequenceEqual(input.BranchFilters)
                ) && 
                (
                    this.Definition == input.Definition ||
                    (this.Definition != null &&
                    this.Definition.Equals(input.Definition))
                ) && 
                (
                    this.RequiresSuccessfulBuild == input.RequiresSuccessfulBuild ||
                    this.RequiresSuccessfulBuild.Equals(input.RequiresSuccessfulBuild)
                ) && 
                (
                    this.TriggerType == input.TriggerType ||
                    this.TriggerType.Equals(input.TriggerType)
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
                if (this.BranchFilters != null)
                {
                    hashCode = (hashCode * 59) + this.BranchFilters.GetHashCode();
                }
                if (this.Definition != null)
                {
                    hashCode = (hashCode * 59) + this.Definition.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.RequiresSuccessfulBuild.GetHashCode();
                hashCode = (hashCode * 59) + this.TriggerType.GetHashCode();
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
