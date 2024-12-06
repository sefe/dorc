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
    /// BuildSummary
    /// </summary>
    [DataContract(Name = "BuildSummary")]
    public partial class BuildSummary : IEquatable<BuildSummary>, IValidatableObject
    {
        /// <summary>
        /// Defines Reason
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum ReasonEnum
        {
            /// <summary>
            /// Enum None for value: none
            /// </summary>
            [EnumMember(Value = "none")]
            None = 1,

            /// <summary>
            /// Enum Manual for value: manual
            /// </summary>
            [EnumMember(Value = "manual")]
            Manual = 2,

            /// <summary>
            /// Enum IndividualCI for value: individualCI
            /// </summary>
            [EnumMember(Value = "individualCI")]
            IndividualCI = 3,

            /// <summary>
            /// Enum BatchedCI for value: batchedCI
            /// </summary>
            [EnumMember(Value = "batchedCI")]
            BatchedCI = 4,

            /// <summary>
            /// Enum Schedule for value: schedule
            /// </summary>
            [EnumMember(Value = "schedule")]
            Schedule = 5,

            /// <summary>
            /// Enum ScheduleForced for value: scheduleForced
            /// </summary>
            [EnumMember(Value = "scheduleForced")]
            ScheduleForced = 6,

            /// <summary>
            /// Enum UserCreated for value: userCreated
            /// </summary>
            [EnumMember(Value = "userCreated")]
            UserCreated = 7,

            /// <summary>
            /// Enum ValidateShelveset for value: validateShelveset
            /// </summary>
            [EnumMember(Value = "validateShelveset")]
            ValidateShelveset = 8,

            /// <summary>
            /// Enum CheckInShelveset for value: checkInShelveset
            /// </summary>
            [EnumMember(Value = "checkInShelveset")]
            CheckInShelveset = 9,

            /// <summary>
            /// Enum PullRequest for value: pullRequest
            /// </summary>
            [EnumMember(Value = "pullRequest")]
            PullRequest = 10,

            /// <summary>
            /// Enum BuildCompletion for value: buildCompletion
            /// </summary>
            [EnumMember(Value = "buildCompletion")]
            BuildCompletion = 11,

            /// <summary>
            /// Enum ResourceTrigger for value: resourceTrigger
            /// </summary>
            [EnumMember(Value = "resourceTrigger")]
            ResourceTrigger = 12,

            /// <summary>
            /// Enum Triggered for value: triggered
            /// </summary>
            [EnumMember(Value = "triggered")]
            Triggered = 13,

            /// <summary>
            /// Enum All for value: all
            /// </summary>
            [EnumMember(Value = "all")]
            All = 14

        }


        /// <summary>
        /// Gets or Sets Reason
        /// </summary>
        [DataMember(Name = "reason", EmitDefaultValue = false)]
        public ReasonEnum? Reason { get; set; }
        /// <summary>
        /// Defines Status
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum StatusEnum
        {
            /// <summary>
            /// Enum None for value: none
            /// </summary>
            [EnumMember(Value = "none")]
            None = 1,

            /// <summary>
            /// Enum InProgress for value: inProgress
            /// </summary>
            [EnumMember(Value = "inProgress")]
            InProgress = 2,

            /// <summary>
            /// Enum Completed for value: completed
            /// </summary>
            [EnumMember(Value = "completed")]
            Completed = 3,

            /// <summary>
            /// Enum Cancelling for value: cancelling
            /// </summary>
            [EnumMember(Value = "cancelling")]
            Cancelling = 4,

            /// <summary>
            /// Enum Postponed for value: postponed
            /// </summary>
            [EnumMember(Value = "postponed")]
            Postponed = 5,

            /// <summary>
            /// Enum NotStarted for value: notStarted
            /// </summary>
            [EnumMember(Value = "notStarted")]
            NotStarted = 6,

            /// <summary>
            /// Enum All for value: all
            /// </summary>
            [EnumMember(Value = "all")]
            All = 7

        }


        /// <summary>
        /// Gets or Sets Status
        /// </summary>
        [DataMember(Name = "status", EmitDefaultValue = false)]
        public StatusEnum? Status { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildSummary" /> class.
        /// </summary>
        /// <param name="build">build.</param>
        /// <param name="finishTime">finishTime.</param>
        /// <param name="keepForever">keepForever.</param>
        /// <param name="quality">quality.</param>
        /// <param name="reason">reason.</param>
        /// <param name="requestedFor">requestedFor.</param>
        /// <param name="startTime">startTime.</param>
        /// <param name="status">status.</param>
        public BuildSummary(XamlBuildReference build = default(XamlBuildReference), DateTime finishTime = default(DateTime), bool keepForever = default(bool), string quality = default(string), ReasonEnum? reason = default(ReasonEnum?), IdentityRef requestedFor = default(IdentityRef), DateTime startTime = default(DateTime), StatusEnum? status = default(StatusEnum?))
        {
            this.Build = build;
            this.FinishTime = finishTime;
            this.KeepForever = keepForever;
            this.Quality = quality;
            this.Reason = reason;
            this.RequestedFor = requestedFor;
            this.StartTime = startTime;
            this.Status = status;
        }

        /// <summary>
        /// Gets or Sets Build
        /// </summary>
        [DataMember(Name = "build", EmitDefaultValue = false)]
        public XamlBuildReference Build { get; set; }

        /// <summary>
        /// Gets or Sets FinishTime
        /// </summary>
        [DataMember(Name = "finishTime", EmitDefaultValue = false)]
        public DateTime FinishTime { get; set; }

        /// <summary>
        /// Gets or Sets KeepForever
        /// </summary>
        [DataMember(Name = "keepForever", EmitDefaultValue = true)]
        public bool KeepForever { get; set; }

        /// <summary>
        /// Gets or Sets Quality
        /// </summary>
        [DataMember(Name = "quality", EmitDefaultValue = false)]
        public string Quality { get; set; }

        /// <summary>
        /// Gets or Sets RequestedFor
        /// </summary>
        [DataMember(Name = "requestedFor", EmitDefaultValue = false)]
        public IdentityRef RequestedFor { get; set; }

        /// <summary>
        /// Gets or Sets StartTime
        /// </summary>
        [DataMember(Name = "startTime", EmitDefaultValue = false)]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildSummary {\n");
            sb.Append("  Build: ").Append(Build).Append("\n");
            sb.Append("  FinishTime: ").Append(FinishTime).Append("\n");
            sb.Append("  KeepForever: ").Append(KeepForever).Append("\n");
            sb.Append("  Quality: ").Append(Quality).Append("\n");
            sb.Append("  Reason: ").Append(Reason).Append("\n");
            sb.Append("  RequestedFor: ").Append(RequestedFor).Append("\n");
            sb.Append("  StartTime: ").Append(StartTime).Append("\n");
            sb.Append("  Status: ").Append(Status).Append("\n");
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
            return this.Equals(input as BuildSummary);
        }

        /// <summary>
        /// Returns true if BuildSummary instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildSummary to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildSummary input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Build == input.Build ||
                    (this.Build != null &&
                    this.Build.Equals(input.Build))
                ) && 
                (
                    this.FinishTime == input.FinishTime ||
                    (this.FinishTime != null &&
                    this.FinishTime.Equals(input.FinishTime))
                ) && 
                (
                    this.KeepForever == input.KeepForever ||
                    this.KeepForever.Equals(input.KeepForever)
                ) && 
                (
                    this.Quality == input.Quality ||
                    (this.Quality != null &&
                    this.Quality.Equals(input.Quality))
                ) && 
                (
                    this.Reason == input.Reason ||
                    this.Reason.Equals(input.Reason)
                ) && 
                (
                    this.RequestedFor == input.RequestedFor ||
                    (this.RequestedFor != null &&
                    this.RequestedFor.Equals(input.RequestedFor))
                ) && 
                (
                    this.StartTime == input.StartTime ||
                    (this.StartTime != null &&
                    this.StartTime.Equals(input.StartTime))
                ) && 
                (
                    this.Status == input.Status ||
                    this.Status.Equals(input.Status)
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
                if (this.Build != null)
                {
                    hashCode = (hashCode * 59) + this.Build.GetHashCode();
                }
                if (this.FinishTime != null)
                {
                    hashCode = (hashCode * 59) + this.FinishTime.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.KeepForever.GetHashCode();
                if (this.Quality != null)
                {
                    hashCode = (hashCode * 59) + this.Quality.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Reason.GetHashCode();
                if (this.RequestedFor != null)
                {
                    hashCode = (hashCode * 59) + this.RequestedFor.GetHashCode();
                }
                if (this.StartTime != null)
                {
                    hashCode = (hashCode * 59) + this.StartTime.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Status.GetHashCode();
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
