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
    /// Represents a phase of a build definition.
    /// </summary>
    [DataContract(Name = "Phase")]
    public partial class Phase : IEquatable<Phase>, IValidatableObject
    {
        /// <summary>
        /// The job authorization scope for builds queued against this definition.
        /// </summary>
        /// <value>The job authorization scope for builds queued against this definition.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum JobAuthorizationScopeEnum
        {
            /// <summary>
            /// Enum ProjectCollection for value: projectCollection
            /// </summary>
            [EnumMember(Value = "projectCollection")]
            ProjectCollection = 1,

            /// <summary>
            /// Enum Project for value: project
            /// </summary>
            [EnumMember(Value = "project")]
            Project = 2

        }


        /// <summary>
        /// The job authorization scope for builds queued against this definition.
        /// </summary>
        /// <value>The job authorization scope for builds queued against this definition.</value>
        [DataMember(Name = "jobAuthorizationScope", EmitDefaultValue = false)]
        public JobAuthorizationScopeEnum? JobAuthorizationScope { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="Phase" /> class.
        /// </summary>
        /// <param name="condition">The condition that must be true for this phase to execute..</param>
        /// <param name="dependencies">dependencies.</param>
        /// <param name="jobAuthorizationScope">The job authorization scope for builds queued against this definition..</param>
        /// <param name="jobCancelTimeoutInMinutes">The cancellation timeout, in minutes, for builds queued against this definition..</param>
        /// <param name="jobTimeoutInMinutes">The job execution timeout, in minutes, for builds queued against this definition..</param>
        /// <param name="name">The name of the phase..</param>
        /// <param name="refName">The unique ref name of the phase..</param>
        /// <param name="steps">steps.</param>
        /// <param name="target">target.</param>
        /// <param name="variables">variables.</param>
        public Phase(string condition = default(string), List<Dependency> dependencies = default(List<Dependency>), JobAuthorizationScopeEnum? jobAuthorizationScope = default(JobAuthorizationScopeEnum?), int jobCancelTimeoutInMinutes = default(int), int jobTimeoutInMinutes = default(int), string name = default(string), string refName = default(string), List<BuildDefinitionStep> steps = default(List<BuildDefinitionStep>), PhaseTarget target = default(PhaseTarget), Dictionary<string, BuildDefinitionVariable> variables = default(Dictionary<string, BuildDefinitionVariable>))
        {
            this.Condition = condition;
            this.Dependencies = dependencies;
            this.JobAuthorizationScope = jobAuthorizationScope;
            this.JobCancelTimeoutInMinutes = jobCancelTimeoutInMinutes;
            this.JobTimeoutInMinutes = jobTimeoutInMinutes;
            this.Name = name;
            this.RefName = refName;
            this.Steps = steps;
            this.Target = target;
            this.Variables = variables;
        }

        /// <summary>
        /// The condition that must be true for this phase to execute.
        /// </summary>
        /// <value>The condition that must be true for this phase to execute.</value>
        [DataMember(Name = "condition", EmitDefaultValue = false)]
        public string Condition { get; set; }

        /// <summary>
        /// Gets or Sets Dependencies
        /// </summary>
        [DataMember(Name = "dependencies", EmitDefaultValue = false)]
        public List<Dependency> Dependencies { get; set; }

        /// <summary>
        /// The cancellation timeout, in minutes, for builds queued against this definition.
        /// </summary>
        /// <value>The cancellation timeout, in minutes, for builds queued against this definition.</value>
        [DataMember(Name = "jobCancelTimeoutInMinutes", EmitDefaultValue = false)]
        public int JobCancelTimeoutInMinutes { get; set; }

        /// <summary>
        /// The job execution timeout, in minutes, for builds queued against this definition.
        /// </summary>
        /// <value>The job execution timeout, in minutes, for builds queued against this definition.</value>
        [DataMember(Name = "jobTimeoutInMinutes", EmitDefaultValue = false)]
        public int JobTimeoutInMinutes { get; set; }

        /// <summary>
        /// The name of the phase.
        /// </summary>
        /// <value>The name of the phase.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// The unique ref name of the phase.
        /// </summary>
        /// <value>The unique ref name of the phase.</value>
        [DataMember(Name = "refName", EmitDefaultValue = false)]
        public string RefName { get; set; }

        /// <summary>
        /// Gets or Sets Steps
        /// </summary>
        [DataMember(Name = "steps", EmitDefaultValue = false)]
        public List<BuildDefinitionStep> Steps { get; set; }

        /// <summary>
        /// Gets or Sets Target
        /// </summary>
        [DataMember(Name = "target", EmitDefaultValue = false)]
        public PhaseTarget Target { get; set; }

        /// <summary>
        /// Gets or Sets Variables
        /// </summary>
        [DataMember(Name = "variables", EmitDefaultValue = false)]
        public Dictionary<string, BuildDefinitionVariable> Variables { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class Phase {\n");
            sb.Append("  Condition: ").Append(Condition).Append("\n");
            sb.Append("  Dependencies: ").Append(Dependencies).Append("\n");
            sb.Append("  JobAuthorizationScope: ").Append(JobAuthorizationScope).Append("\n");
            sb.Append("  JobCancelTimeoutInMinutes: ").Append(JobCancelTimeoutInMinutes).Append("\n");
            sb.Append("  JobTimeoutInMinutes: ").Append(JobTimeoutInMinutes).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  RefName: ").Append(RefName).Append("\n");
            sb.Append("  Steps: ").Append(Steps).Append("\n");
            sb.Append("  Target: ").Append(Target).Append("\n");
            sb.Append("  Variables: ").Append(Variables).Append("\n");
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
            return this.Equals(input as Phase);
        }

        /// <summary>
        /// Returns true if Phase instances are equal
        /// </summary>
        /// <param name="input">Instance of Phase to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(Phase input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Condition == input.Condition ||
                    (this.Condition != null &&
                    this.Condition.Equals(input.Condition))
                ) && 
                (
                    this.Dependencies == input.Dependencies ||
                    this.Dependencies != null &&
                    input.Dependencies != null &&
                    this.Dependencies.SequenceEqual(input.Dependencies)
                ) && 
                (
                    this.JobAuthorizationScope == input.JobAuthorizationScope ||
                    this.JobAuthorizationScope.Equals(input.JobAuthorizationScope)
                ) && 
                (
                    this.JobCancelTimeoutInMinutes == input.JobCancelTimeoutInMinutes ||
                    this.JobCancelTimeoutInMinutes.Equals(input.JobCancelTimeoutInMinutes)
                ) && 
                (
                    this.JobTimeoutInMinutes == input.JobTimeoutInMinutes ||
                    this.JobTimeoutInMinutes.Equals(input.JobTimeoutInMinutes)
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.RefName == input.RefName ||
                    (this.RefName != null &&
                    this.RefName.Equals(input.RefName))
                ) && 
                (
                    this.Steps == input.Steps ||
                    this.Steps != null &&
                    input.Steps != null &&
                    this.Steps.SequenceEqual(input.Steps)
                ) && 
                (
                    this.Target == input.Target ||
                    (this.Target != null &&
                    this.Target.Equals(input.Target))
                ) && 
                (
                    this.Variables == input.Variables ||
                    this.Variables != null &&
                    input.Variables != null &&
                    this.Variables.SequenceEqual(input.Variables)
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
                if (this.Condition != null)
                {
                    hashCode = (hashCode * 59) + this.Condition.GetHashCode();
                }
                if (this.Dependencies != null)
                {
                    hashCode = (hashCode * 59) + this.Dependencies.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.JobAuthorizationScope.GetHashCode();
                hashCode = (hashCode * 59) + this.JobCancelTimeoutInMinutes.GetHashCode();
                hashCode = (hashCode * 59) + this.JobTimeoutInMinutes.GetHashCode();
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                if (this.RefName != null)
                {
                    hashCode = (hashCode * 59) + this.RefName.GetHashCode();
                }
                if (this.Steps != null)
                {
                    hashCode = (hashCode * 59) + this.Steps.GetHashCode();
                }
                if (this.Target != null)
                {
                    hashCode = (hashCode * 59) + this.Target.GetHashCode();
                }
                if (this.Variables != null)
                {
                    hashCode = (hashCode * 59) + this.Variables.GetHashCode();
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
