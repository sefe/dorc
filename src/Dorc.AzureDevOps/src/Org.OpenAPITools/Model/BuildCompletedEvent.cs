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
    /// BuildCompletedEvent
    /// </summary>
    [DataContract(Name = "BuildCompletedEvent")]
    public partial class BuildCompletedEvent : IEquatable<BuildCompletedEvent>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCompletedEvent" /> class.
        /// </summary>
        /// <param name="changes">Changes associated with a build used for build notifications.</param>
        /// <param name="pullRequest">pullRequest.</param>
        /// <param name="testResults">testResults.</param>
        /// <param name="timelineRecords">Timeline records associated with a build used for build notifications.</param>
        /// <param name="workItems">Work items associated with a build used for build notifications.</param>
        /// <param name="buildId">buildId.</param>
        public BuildCompletedEvent(List<Change> changes = default(List<Change>), PullRequest pullRequest = default(PullRequest), AggregatedResultsAnalysis testResults = default(AggregatedResultsAnalysis), List<TimelineRecord> timelineRecords = default(List<TimelineRecord>), List<AssociatedWorkItem> workItems = default(List<AssociatedWorkItem>), int buildId = default(int))
        {
            this.Changes = changes;
            this.PullRequest = pullRequest;
            this.TestResults = testResults;
            this.TimelineRecords = timelineRecords;
            this.WorkItems = workItems;
            this.BuildId = buildId;
        }

        /// <summary>
        /// Changes associated with a build used for build notifications
        /// </summary>
        /// <value>Changes associated with a build used for build notifications</value>
        [DataMember(Name = "changes", EmitDefaultValue = false)]
        public List<Change> Changes { get; set; }

        /// <summary>
        /// Gets or Sets PullRequest
        /// </summary>
        [DataMember(Name = "pullRequest", EmitDefaultValue = false)]
        public PullRequest PullRequest { get; set; }

        /// <summary>
        /// Gets or Sets TestResults
        /// </summary>
        [DataMember(Name = "testResults", EmitDefaultValue = false)]
        public AggregatedResultsAnalysis TestResults { get; set; }

        /// <summary>
        /// Timeline records associated with a build used for build notifications
        /// </summary>
        /// <value>Timeline records associated with a build used for build notifications</value>
        [DataMember(Name = "timelineRecords", EmitDefaultValue = false)]
        public List<TimelineRecord> TimelineRecords { get; set; }

        /// <summary>
        /// Work items associated with a build used for build notifications
        /// </summary>
        /// <value>Work items associated with a build used for build notifications</value>
        [DataMember(Name = "workItems", EmitDefaultValue = false)]
        public List<AssociatedWorkItem> WorkItems { get; set; }

        /// <summary>
        /// Gets or Sets BuildId
        /// </summary>
        [DataMember(Name = "buildId", EmitDefaultValue = false)]
        public int BuildId { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildCompletedEvent {\n");
            sb.Append("  Changes: ").Append(Changes).Append("\n");
            sb.Append("  PullRequest: ").Append(PullRequest).Append("\n");
            sb.Append("  TestResults: ").Append(TestResults).Append("\n");
            sb.Append("  TimelineRecords: ").Append(TimelineRecords).Append("\n");
            sb.Append("  WorkItems: ").Append(WorkItems).Append("\n");
            sb.Append("  BuildId: ").Append(BuildId).Append("\n");
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
            return this.Equals(input as BuildCompletedEvent);
        }

        /// <summary>
        /// Returns true if BuildCompletedEvent instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildCompletedEvent to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildCompletedEvent input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Changes == input.Changes ||
                    this.Changes != null &&
                    input.Changes != null &&
                    this.Changes.SequenceEqual(input.Changes)
                ) && 
                (
                    this.PullRequest == input.PullRequest ||
                    (this.PullRequest != null &&
                    this.PullRequest.Equals(input.PullRequest))
                ) && 
                (
                    this.TestResults == input.TestResults ||
                    (this.TestResults != null &&
                    this.TestResults.Equals(input.TestResults))
                ) && 
                (
                    this.TimelineRecords == input.TimelineRecords ||
                    this.TimelineRecords != null &&
                    input.TimelineRecords != null &&
                    this.TimelineRecords.SequenceEqual(input.TimelineRecords)
                ) && 
                (
                    this.WorkItems == input.WorkItems ||
                    this.WorkItems != null &&
                    input.WorkItems != null &&
                    this.WorkItems.SequenceEqual(input.WorkItems)
                ) && 
                (
                    this.BuildId == input.BuildId ||
                    this.BuildId.Equals(input.BuildId)
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
                if (this.Changes != null)
                {
                    hashCode = (hashCode * 59) + this.Changes.GetHashCode();
                }
                if (this.PullRequest != null)
                {
                    hashCode = (hashCode * 59) + this.PullRequest.GetHashCode();
                }
                if (this.TestResults != null)
                {
                    hashCode = (hashCode * 59) + this.TestResults.GetHashCode();
                }
                if (this.TimelineRecords != null)
                {
                    hashCode = (hashCode * 59) + this.TimelineRecords.GetHashCode();
                }
                if (this.WorkItems != null)
                {
                    hashCode = (hashCode * 59) + this.WorkItems.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.BuildId.GetHashCode();
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
