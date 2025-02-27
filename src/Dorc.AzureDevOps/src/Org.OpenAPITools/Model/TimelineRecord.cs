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
    /// Represents an entry in a build&#39;s timeline.
    /// </summary>
    [DataContract(Name = "TimelineRecord")]
    public partial class TimelineRecord : IEquatable<TimelineRecord>, IValidatableObject
    {
        /// <summary>
        /// The result.
        /// </summary>
        /// <value>The result.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum ResultEnum
        {
            /// <summary>
            /// Enum Succeeded for value: succeeded
            /// </summary>
            [EnumMember(Value = "succeeded")]
            Succeeded = 1,

            /// <summary>
            /// Enum SucceededWithIssues for value: succeededWithIssues
            /// </summary>
            [EnumMember(Value = "succeededWithIssues")]
            SucceededWithIssues = 2,

            /// <summary>
            /// Enum Failed for value: failed
            /// </summary>
            [EnumMember(Value = "failed")]
            Failed = 3,

            /// <summary>
            /// Enum Canceled for value: canceled
            /// </summary>
            [EnumMember(Value = "canceled")]
            Canceled = 4,

            /// <summary>
            /// Enum Skipped for value: skipped
            /// </summary>
            [EnumMember(Value = "skipped")]
            Skipped = 5,

            /// <summary>
            /// Enum Abandoned for value: abandoned
            /// </summary>
            [EnumMember(Value = "abandoned")]
            Abandoned = 6

        }


        /// <summary>
        /// The result.
        /// </summary>
        /// <value>The result.</value>
        [DataMember(Name = "result", EmitDefaultValue = false)]
        public ResultEnum? Result { get; set; }
        /// <summary>
        /// The state of the record.
        /// </summary>
        /// <value>The state of the record.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum StateEnum
        {
            /// <summary>
            /// Enum Pending for value: pending
            /// </summary>
            [EnumMember(Value = "pending")]
            Pending = 1,

            /// <summary>
            /// Enum InProgress for value: inProgress
            /// </summary>
            [EnumMember(Value = "inProgress")]
            InProgress = 2,

            /// <summary>
            /// Enum Completed for value: completed
            /// </summary>
            [EnumMember(Value = "completed")]
            Completed = 3

        }


        /// <summary>
        /// The state of the record.
        /// </summary>
        /// <value>The state of the record.</value>
        [DataMember(Name = "state", EmitDefaultValue = false)]
        public StateEnum? State { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="TimelineRecord" /> class.
        /// </summary>
        /// <param name="links">links.</param>
        /// <param name="attempt">Attempt number of record..</param>
        /// <param name="changeId">The change ID..</param>
        /// <param name="currentOperation">A string that indicates the current operation..</param>
        /// <param name="details">details.</param>
        /// <param name="errorCount">The number of errors produced by this operation..</param>
        /// <param name="finishTime">The finish time..</param>
        /// <param name="id">The ID of the record..</param>
        /// <param name="identifier">String identifier that is consistent across attempts..</param>
        /// <param name="issues">issues.</param>
        /// <param name="lastModified">The time the record was last modified..</param>
        /// <param name="log">log.</param>
        /// <param name="name">The name..</param>
        /// <param name="order">An ordinal value relative to other records..</param>
        /// <param name="parentId">The ID of the record&#39;s parent..</param>
        /// <param name="percentComplete">The current completion percentage..</param>
        /// <param name="previousAttempts">previousAttempts.</param>
        /// <param name="queueId">The queue ID of the queue that the operation ran on..</param>
        /// <param name="result">The result..</param>
        /// <param name="resultCode">The result code..</param>
        /// <param name="startTime">The start time..</param>
        /// <param name="state">The state of the record..</param>
        /// <param name="task">task.</param>
        /// <param name="type">The type of the record..</param>
        /// <param name="url">The REST URL of the timeline record..</param>
        /// <param name="warningCount">The number of warnings produced by this operation..</param>
        /// <param name="workerName">The name of the agent running the operation..</param>
        public TimelineRecord(ReferenceLinks links = default(ReferenceLinks), int attempt = default(int), int changeId = default(int), string currentOperation = default(string), TimelineReference details = default(TimelineReference), int errorCount = default(int), DateTime finishTime = default(DateTime), Guid id = default(Guid), string identifier = default(string), List<Issue> issues = default(List<Issue>), DateTime lastModified = default(DateTime), BuildLogReference log = default(BuildLogReference), string name = default(string), int order = default(int), Guid parentId = default(Guid), int percentComplete = default(int), List<TimelineAttempt> previousAttempts = default(List<TimelineAttempt>), int queueId = default(int), ResultEnum? result = default(ResultEnum?), string resultCode = default(string), DateTime startTime = default(DateTime), StateEnum? state = default(StateEnum?), TaskReference task = default(TaskReference), string type = default(string), string url = default(string), int warningCount = default(int), string workerName = default(string))
        {
            this.Links = links;
            this.Attempt = attempt;
            this.ChangeId = changeId;
            this.CurrentOperation = currentOperation;
            this.Details = details;
            this.ErrorCount = errorCount;
            this.FinishTime = finishTime;
            this.Id = id;
            this.Identifier = identifier;
            this.Issues = issues;
            this.LastModified = lastModified;
            this.Log = log;
            this.Name = name;
            this.Order = order;
            this.ParentId = parentId;
            this.PercentComplete = percentComplete;
            this.PreviousAttempts = previousAttempts;
            this.QueueId = queueId;
            this.Result = result;
            this.ResultCode = resultCode;
            this.StartTime = startTime;
            this.State = state;
            this.Task = task;
            this.Type = type;
            this.Url = url;
            this.WarningCount = warningCount;
            this.WorkerName = workerName;
        }

        /// <summary>
        /// Gets or Sets Links
        /// </summary>
        [DataMember(Name = "_links", EmitDefaultValue = false)]
        public ReferenceLinks Links { get; set; }

        /// <summary>
        /// Attempt number of record.
        /// </summary>
        /// <value>Attempt number of record.</value>
        [DataMember(Name = "attempt", EmitDefaultValue = false)]
        public int Attempt { get; set; }

        /// <summary>
        /// The change ID.
        /// </summary>
        /// <value>The change ID.</value>
        [DataMember(Name = "changeId", EmitDefaultValue = false)]
        public int ChangeId { get; set; }

        /// <summary>
        /// A string that indicates the current operation.
        /// </summary>
        /// <value>A string that indicates the current operation.</value>
        [DataMember(Name = "currentOperation", EmitDefaultValue = false)]
        public string CurrentOperation { get; set; }

        /// <summary>
        /// Gets or Sets Details
        /// </summary>
        [DataMember(Name = "details", EmitDefaultValue = false)]
        public TimelineReference Details { get; set; }

        /// <summary>
        /// The number of errors produced by this operation.
        /// </summary>
        /// <value>The number of errors produced by this operation.</value>
        [DataMember(Name = "errorCount", EmitDefaultValue = false)]
        public int ErrorCount { get; set; }

        /// <summary>
        /// The finish time.
        /// </summary>
        /// <value>The finish time.</value>
        [DataMember(Name = "finishTime", EmitDefaultValue = false)]
        public DateTime FinishTime { get; set; }

        /// <summary>
        /// The ID of the record.
        /// </summary>
        /// <value>The ID of the record.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public Guid Id { get; set; }

        /// <summary>
        /// String identifier that is consistent across attempts.
        /// </summary>
        /// <value>String identifier that is consistent across attempts.</value>
        [DataMember(Name = "identifier", EmitDefaultValue = false)]
        public string Identifier { get; set; }

        /// <summary>
        /// Gets or Sets Issues
        /// </summary>
        [DataMember(Name = "issues", EmitDefaultValue = false)]
        public List<Issue> Issues { get; set; }

        /// <summary>
        /// The time the record was last modified.
        /// </summary>
        /// <value>The time the record was last modified.</value>
        [DataMember(Name = "lastModified", EmitDefaultValue = false)]
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Gets or Sets Log
        /// </summary>
        [DataMember(Name = "log", EmitDefaultValue = false)]
        public BuildLogReference Log { get; set; }

        /// <summary>
        /// The name.
        /// </summary>
        /// <value>The name.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// An ordinal value relative to other records.
        /// </summary>
        /// <value>An ordinal value relative to other records.</value>
        [DataMember(Name = "order", EmitDefaultValue = false)]
        public int Order { get; set; }

        /// <summary>
        /// The ID of the record&#39;s parent.
        /// </summary>
        /// <value>The ID of the record&#39;s parent.</value>
        [DataMember(Name = "parentId", EmitDefaultValue = false)]
        public Guid ParentId { get; set; }

        /// <summary>
        /// The current completion percentage.
        /// </summary>
        /// <value>The current completion percentage.</value>
        [DataMember(Name = "percentComplete", EmitDefaultValue = false)]
        public int PercentComplete { get; set; }

        /// <summary>
        /// Gets or Sets PreviousAttempts
        /// </summary>
        [DataMember(Name = "previousAttempts", EmitDefaultValue = false)]
        public List<TimelineAttempt> PreviousAttempts { get; set; }

        /// <summary>
        /// The queue ID of the queue that the operation ran on.
        /// </summary>
        /// <value>The queue ID of the queue that the operation ran on.</value>
        [DataMember(Name = "queueId", EmitDefaultValue = false)]
        public int QueueId { get; set; }

        /// <summary>
        /// The result code.
        /// </summary>
        /// <value>The result code.</value>
        [DataMember(Name = "resultCode", EmitDefaultValue = false)]
        public string ResultCode { get; set; }

        /// <summary>
        /// The start time.
        /// </summary>
        /// <value>The start time.</value>
        [DataMember(Name = "startTime", EmitDefaultValue = false)]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or Sets Task
        /// </summary>
        [DataMember(Name = "task", EmitDefaultValue = false)]
        public TaskReference Task { get; set; }

        /// <summary>
        /// The type of the record.
        /// </summary>
        /// <value>The type of the record.</value>
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public string Type { get; set; }

        /// <summary>
        /// The REST URL of the timeline record.
        /// </summary>
        /// <value>The REST URL of the timeline record.</value>
        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        /// <summary>
        /// The number of warnings produced by this operation.
        /// </summary>
        /// <value>The number of warnings produced by this operation.</value>
        [DataMember(Name = "warningCount", EmitDefaultValue = false)]
        public int WarningCount { get; set; }

        /// <summary>
        /// The name of the agent running the operation.
        /// </summary>
        /// <value>The name of the agent running the operation.</value>
        [DataMember(Name = "workerName", EmitDefaultValue = false)]
        public string WorkerName { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class TimelineRecord {\n");
            sb.Append("  Links: ").Append(Links).Append("\n");
            sb.Append("  Attempt: ").Append(Attempt).Append("\n");
            sb.Append("  ChangeId: ").Append(ChangeId).Append("\n");
            sb.Append("  CurrentOperation: ").Append(CurrentOperation).Append("\n");
            sb.Append("  Details: ").Append(Details).Append("\n");
            sb.Append("  ErrorCount: ").Append(ErrorCount).Append("\n");
            sb.Append("  FinishTime: ").Append(FinishTime).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Identifier: ").Append(Identifier).Append("\n");
            sb.Append("  Issues: ").Append(Issues).Append("\n");
            sb.Append("  LastModified: ").Append(LastModified).Append("\n");
            sb.Append("  Log: ").Append(Log).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Order: ").Append(Order).Append("\n");
            sb.Append("  ParentId: ").Append(ParentId).Append("\n");
            sb.Append("  PercentComplete: ").Append(PercentComplete).Append("\n");
            sb.Append("  PreviousAttempts: ").Append(PreviousAttempts).Append("\n");
            sb.Append("  QueueId: ").Append(QueueId).Append("\n");
            sb.Append("  Result: ").Append(Result).Append("\n");
            sb.Append("  ResultCode: ").Append(ResultCode).Append("\n");
            sb.Append("  StartTime: ").Append(StartTime).Append("\n");
            sb.Append("  State: ").Append(State).Append("\n");
            sb.Append("  Task: ").Append(Task).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
            sb.Append("  Url: ").Append(Url).Append("\n");
            sb.Append("  WarningCount: ").Append(WarningCount).Append("\n");
            sb.Append("  WorkerName: ").Append(WorkerName).Append("\n");
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
            return this.Equals(input as TimelineRecord);
        }

        /// <summary>
        /// Returns true if TimelineRecord instances are equal
        /// </summary>
        /// <param name="input">Instance of TimelineRecord to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(TimelineRecord input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Links == input.Links ||
                    (this.Links != null &&
                    this.Links.Equals(input.Links))
                ) && 
                (
                    this.Attempt == input.Attempt ||
                    this.Attempt.Equals(input.Attempt)
                ) && 
                (
                    this.ChangeId == input.ChangeId ||
                    this.ChangeId.Equals(input.ChangeId)
                ) && 
                (
                    this.CurrentOperation == input.CurrentOperation ||
                    (this.CurrentOperation != null &&
                    this.CurrentOperation.Equals(input.CurrentOperation))
                ) && 
                (
                    this.Details == input.Details ||
                    (this.Details != null &&
                    this.Details.Equals(input.Details))
                ) && 
                (
                    this.ErrorCount == input.ErrorCount ||
                    this.ErrorCount.Equals(input.ErrorCount)
                ) && 
                (
                    this.FinishTime == input.FinishTime ||
                    (this.FinishTime != null &&
                    this.FinishTime.Equals(input.FinishTime))
                ) && 
                (
                    this.Id == input.Id ||
                    (this.Id != null &&
                    this.Id.Equals(input.Id))
                ) && 
                (
                    this.Identifier == input.Identifier ||
                    (this.Identifier != null &&
                    this.Identifier.Equals(input.Identifier))
                ) && 
                (
                    this.Issues == input.Issues ||
                    this.Issues != null &&
                    input.Issues != null &&
                    this.Issues.SequenceEqual(input.Issues)
                ) && 
                (
                    this.LastModified == input.LastModified ||
                    (this.LastModified != null &&
                    this.LastModified.Equals(input.LastModified))
                ) && 
                (
                    this.Log == input.Log ||
                    (this.Log != null &&
                    this.Log.Equals(input.Log))
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.Order == input.Order ||
                    this.Order.Equals(input.Order)
                ) && 
                (
                    this.ParentId == input.ParentId ||
                    (this.ParentId != null &&
                    this.ParentId.Equals(input.ParentId))
                ) && 
                (
                    this.PercentComplete == input.PercentComplete ||
                    this.PercentComplete.Equals(input.PercentComplete)
                ) && 
                (
                    this.PreviousAttempts == input.PreviousAttempts ||
                    this.PreviousAttempts != null &&
                    input.PreviousAttempts != null &&
                    this.PreviousAttempts.SequenceEqual(input.PreviousAttempts)
                ) && 
                (
                    this.QueueId == input.QueueId ||
                    this.QueueId.Equals(input.QueueId)
                ) && 
                (
                    this.Result == input.Result ||
                    this.Result.Equals(input.Result)
                ) && 
                (
                    this.ResultCode == input.ResultCode ||
                    (this.ResultCode != null &&
                    this.ResultCode.Equals(input.ResultCode))
                ) && 
                (
                    this.StartTime == input.StartTime ||
                    (this.StartTime != null &&
                    this.StartTime.Equals(input.StartTime))
                ) && 
                (
                    this.State == input.State ||
                    this.State.Equals(input.State)
                ) && 
                (
                    this.Task == input.Task ||
                    (this.Task != null &&
                    this.Task.Equals(input.Task))
                ) && 
                (
                    this.Type == input.Type ||
                    (this.Type != null &&
                    this.Type.Equals(input.Type))
                ) && 
                (
                    this.Url == input.Url ||
                    (this.Url != null &&
                    this.Url.Equals(input.Url))
                ) && 
                (
                    this.WarningCount == input.WarningCount ||
                    this.WarningCount.Equals(input.WarningCount)
                ) && 
                (
                    this.WorkerName == input.WorkerName ||
                    (this.WorkerName != null &&
                    this.WorkerName.Equals(input.WorkerName))
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
                if (this.Links != null)
                {
                    hashCode = (hashCode * 59) + this.Links.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Attempt.GetHashCode();
                hashCode = (hashCode * 59) + this.ChangeId.GetHashCode();
                if (this.CurrentOperation != null)
                {
                    hashCode = (hashCode * 59) + this.CurrentOperation.GetHashCode();
                }
                if (this.Details != null)
                {
                    hashCode = (hashCode * 59) + this.Details.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.ErrorCount.GetHashCode();
                if (this.FinishTime != null)
                {
                    hashCode = (hashCode * 59) + this.FinishTime.GetHashCode();
                }
                if (this.Id != null)
                {
                    hashCode = (hashCode * 59) + this.Id.GetHashCode();
                }
                if (this.Identifier != null)
                {
                    hashCode = (hashCode * 59) + this.Identifier.GetHashCode();
                }
                if (this.Issues != null)
                {
                    hashCode = (hashCode * 59) + this.Issues.GetHashCode();
                }
                if (this.LastModified != null)
                {
                    hashCode = (hashCode * 59) + this.LastModified.GetHashCode();
                }
                if (this.Log != null)
                {
                    hashCode = (hashCode * 59) + this.Log.GetHashCode();
                }
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Order.GetHashCode();
                if (this.ParentId != null)
                {
                    hashCode = (hashCode * 59) + this.ParentId.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.PercentComplete.GetHashCode();
                if (this.PreviousAttempts != null)
                {
                    hashCode = (hashCode * 59) + this.PreviousAttempts.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.QueueId.GetHashCode();
                hashCode = (hashCode * 59) + this.Result.GetHashCode();
                if (this.ResultCode != null)
                {
                    hashCode = (hashCode * 59) + this.ResultCode.GetHashCode();
                }
                if (this.StartTime != null)
                {
                    hashCode = (hashCode * 59) + this.StartTime.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.State.GetHashCode();
                if (this.Task != null)
                {
                    hashCode = (hashCode * 59) + this.Task.GetHashCode();
                }
                if (this.Type != null)
                {
                    hashCode = (hashCode * 59) + this.Type.GetHashCode();
                }
                if (this.Url != null)
                {
                    hashCode = (hashCode * 59) + this.Url.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.WarningCount.GetHashCode();
                if (this.WorkerName != null)
                {
                    hashCode = (hashCode * 59) + this.WorkerName.GetHashCode();
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
