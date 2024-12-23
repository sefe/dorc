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
    /// Represents a build definition.
    /// </summary>
    [DataContract(Name = "BuildDefinition")]
    public partial class BuildDefinition : IEquatable<BuildDefinition>, IValidatableObject
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
        /// A value that indicates whether builds can be queued against this definition.
        /// </summary>
        /// <value>A value that indicates whether builds can be queued against this definition.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum QueueStatusEnum
        {
            /// <summary>
            /// Enum Enabled for value: enabled
            /// </summary>
            [EnumMember(Value = "enabled")]
            Enabled = 1,

            /// <summary>
            /// Enum Paused for value: paused
            /// </summary>
            [EnumMember(Value = "paused")]
            Paused = 2,

            /// <summary>
            /// Enum Disabled for value: disabled
            /// </summary>
            [EnumMember(Value = "disabled")]
            Disabled = 3

        }


        /// <summary>
        /// A value that indicates whether builds can be queued against this definition.
        /// </summary>
        /// <value>A value that indicates whether builds can be queued against this definition.</value>
        [DataMember(Name = "queueStatus", EmitDefaultValue = false)]
        public QueueStatusEnum? QueueStatus { get; set; }
        /// <summary>
        /// The type of the definition.
        /// </summary>
        /// <value>The type of the definition.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum TypeEnum
        {
            /// <summary>
            /// Enum Xaml for value: xaml
            /// </summary>
            [EnumMember(Value = "xaml")]
            Xaml = 1,

            /// <summary>
            /// Enum Build for value: build
            /// </summary>
            [EnumMember(Value = "build")]
            Build = 2

        }


        /// <summary>
        /// The type of the definition.
        /// </summary>
        /// <value>The type of the definition.</value>
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public TypeEnum? Type { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildDefinition" /> class.
        /// </summary>
        /// <param name="badgeEnabled">Indicates whether badges are enabled for this definition..</param>
        /// <param name="buildNumberFormat">The build number format..</param>
        /// <param name="comment">A save-time comment for the definition..</param>
        /// <param name="demands">demands.</param>
        /// <param name="description">The description..</param>
        /// <param name="dropLocation">The drop location for the definition..</param>
        /// <param name="jobAuthorizationScope">The job authorization scope for builds queued against this definition..</param>
        /// <param name="jobCancelTimeoutInMinutes">The job cancel timeout (in minutes) for builds cancelled by user for this definition..</param>
        /// <param name="jobTimeoutInMinutes">The job execution timeout (in minutes) for builds queued against this definition..</param>
        /// <param name="options">options.</param>
        /// <param name="process">process.</param>
        /// <param name="processParameters">processParameters.</param>
        /// <param name="properties">properties.</param>
        /// <param name="repository">repository.</param>
        /// <param name="retentionRules">retentionRules.</param>
        /// <param name="tags">tags.</param>
        /// <param name="triggers">triggers.</param>
        /// <param name="variableGroups">variableGroups.</param>
        /// <param name="variables">variables.</param>
        /// <param name="createdDate">The date this version of the definition was created..</param>
        /// <param name="id">The ID of the referenced definition..</param>
        /// <param name="name">The name of the referenced definition..</param>
        /// <param name="path">The folder path of the definition..</param>
        /// <param name="project">project.</param>
        /// <param name="queueStatus">A value that indicates whether builds can be queued against this definition..</param>
        /// <param name="revision">The definition revision number..</param>
        /// <param name="type">The type of the definition..</param>
        /// <param name="uri">The definition&#39;s URI..</param>
        /// <param name="url">The REST URL of the definition..</param>
        public BuildDefinition(bool badgeEnabled = default(bool), string buildNumberFormat = default(string), string comment = default(string), List<Demand> demands = default(List<Demand>), string description = default(string), string dropLocation = default(string), JobAuthorizationScopeEnum? jobAuthorizationScope = default(JobAuthorizationScopeEnum?), int jobCancelTimeoutInMinutes = default(int), int jobTimeoutInMinutes = default(int), List<BuildOption> options = default(List<BuildOption>), BuildProcess process = default(BuildProcess), ProcessParameters processParameters = default(ProcessParameters), PropertiesCollection properties = default(PropertiesCollection), BuildRepository repository = default(BuildRepository), List<RetentionPolicy> retentionRules = default(List<RetentionPolicy>), List<string> tags = default(List<string>), List<BuildTrigger> triggers = default(List<BuildTrigger>), List<VariableGroup> variableGroups = default(List<VariableGroup>), Dictionary<string, BuildDefinitionVariable> variables = default(Dictionary<string, BuildDefinitionVariable>), DateTime createdDate = default(DateTime), int id = default(int), string name = default(string), string path = default(string), TeamProjectReference project = default(TeamProjectReference), QueueStatusEnum? queueStatus = default(QueueStatusEnum?), int revision = default(int), TypeEnum? type = default(TypeEnum?), string uri = default(string), string url = default(string))
        {
            this.BadgeEnabled = badgeEnabled;
            this.BuildNumberFormat = buildNumberFormat;
            this.Comment = comment;
            this.Demands = demands;
            this.Description = description;
            this.DropLocation = dropLocation;
            this.JobAuthorizationScope = jobAuthorizationScope;
            this.JobCancelTimeoutInMinutes = jobCancelTimeoutInMinutes;
            this.JobTimeoutInMinutes = jobTimeoutInMinutes;
            this.Options = options;
            this.Process = process;
            this.ProcessParameters = processParameters;
            this.Properties = properties;
            this.Repository = repository;
            this.RetentionRules = retentionRules;
            this.Tags = tags;
            this.Triggers = triggers;
            this.VariableGroups = variableGroups;
            this.Variables = variables;
            this.CreatedDate = createdDate;
            this.Id = id;
            this.Name = name;
            this.Path = path;
            this.Project = project;
            this.QueueStatus = queueStatus;
            this.Revision = revision;
            this.Type = type;
            this.Uri = uri;
            this.Url = url;
        }

        /// <summary>
        /// Indicates whether badges are enabled for this definition.
        /// </summary>
        /// <value>Indicates whether badges are enabled for this definition.</value>
        [DataMember(Name = "badgeEnabled", EmitDefaultValue = true)]
        public bool BadgeEnabled { get; set; }

        /// <summary>
        /// The build number format.
        /// </summary>
        /// <value>The build number format.</value>
        [DataMember(Name = "buildNumberFormat", EmitDefaultValue = false)]
        public string BuildNumberFormat { get; set; }

        /// <summary>
        /// A save-time comment for the definition.
        /// </summary>
        /// <value>A save-time comment for the definition.</value>
        [DataMember(Name = "comment", EmitDefaultValue = false)]
        public string Comment { get; set; }

        /// <summary>
        /// Gets or Sets Demands
        /// </summary>
        [DataMember(Name = "demands", EmitDefaultValue = false)]
        public List<Demand> Demands { get; set; }

        /// <summary>
        /// The description.
        /// </summary>
        /// <value>The description.</value>
        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string Description { get; set; }

        /// <summary>
        /// The drop location for the definition.
        /// </summary>
        /// <value>The drop location for the definition.</value>
        [DataMember(Name = "dropLocation", EmitDefaultValue = false)]
        public string DropLocation { get; set; }

        /// <summary>
        /// The job cancel timeout (in minutes) for builds cancelled by user for this definition.
        /// </summary>
        /// <value>The job cancel timeout (in minutes) for builds cancelled by user for this definition.</value>
        [DataMember(Name = "jobCancelTimeoutInMinutes", EmitDefaultValue = false)]
        public int JobCancelTimeoutInMinutes { get; set; }

        /// <summary>
        /// The job execution timeout (in minutes) for builds queued against this definition.
        /// </summary>
        /// <value>The job execution timeout (in minutes) for builds queued against this definition.</value>
        [DataMember(Name = "jobTimeoutInMinutes", EmitDefaultValue = false)]
        public int JobTimeoutInMinutes { get; set; }

        /// <summary>
        /// Gets or Sets Options
        /// </summary>
        [DataMember(Name = "options", EmitDefaultValue = false)]
        public List<BuildOption> Options { get; set; }

        /// <summary>
        /// Gets or Sets Process
        /// </summary>
        [DataMember(Name = "process", EmitDefaultValue = false)]
        public BuildProcess Process { get; set; }

        /// <summary>
        /// Gets or Sets ProcessParameters
        /// </summary>
        [DataMember(Name = "processParameters", EmitDefaultValue = false)]
        public ProcessParameters ProcessParameters { get; set; }

        /// <summary>
        /// Gets or Sets Properties
        /// </summary>
        [DataMember(Name = "properties", EmitDefaultValue = false)]
        public PropertiesCollection Properties { get; set; }

        /// <summary>
        /// Gets or Sets Repository
        /// </summary>
        [DataMember(Name = "repository", EmitDefaultValue = false)]
        public BuildRepository Repository { get; set; }

        /// <summary>
        /// Gets or Sets RetentionRules
        /// </summary>
        [DataMember(Name = "retentionRules", EmitDefaultValue = false)]
        public List<RetentionPolicy> RetentionRules { get; set; }

        /// <summary>
        /// Gets or Sets Tags
        /// </summary>
        [DataMember(Name = "tags", EmitDefaultValue = false)]
        public List<string> Tags { get; set; }

        /// <summary>
        /// Gets or Sets Triggers
        /// </summary>
        [DataMember(Name = "triggers", EmitDefaultValue = false)]
        public List<BuildTrigger> Triggers { get; set; }

        /// <summary>
        /// Gets or Sets VariableGroups
        /// </summary>
        [DataMember(Name = "variableGroups", EmitDefaultValue = false)]
        public List<VariableGroup> VariableGroups { get; set; }

        /// <summary>
        /// Gets or Sets Variables
        /// </summary>
        [DataMember(Name = "variables", EmitDefaultValue = false)]
        public Dictionary<string, BuildDefinitionVariable> Variables { get; set; }

        /// <summary>
        /// The date this version of the definition was created.
        /// </summary>
        /// <value>The date this version of the definition was created.</value>
        [DataMember(Name = "createdDate", EmitDefaultValue = false)]
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// The ID of the referenced definition.
        /// </summary>
        /// <value>The ID of the referenced definition.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public int Id { get; set; }

        /// <summary>
        /// The name of the referenced definition.
        /// </summary>
        /// <value>The name of the referenced definition.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// The folder path of the definition.
        /// </summary>
        /// <value>The folder path of the definition.</value>
        [DataMember(Name = "path", EmitDefaultValue = false)]
        public string Path { get; set; }

        /// <summary>
        /// Gets or Sets Project
        /// </summary>
        [DataMember(Name = "project", EmitDefaultValue = false)]
        public TeamProjectReference Project { get; set; }

        /// <summary>
        /// The definition revision number.
        /// </summary>
        /// <value>The definition revision number.</value>
        [DataMember(Name = "revision", EmitDefaultValue = false)]
        public int Revision { get; set; }

        /// <summary>
        /// The definition&#39;s URI.
        /// </summary>
        /// <value>The definition&#39;s URI.</value>
        [DataMember(Name = "uri", EmitDefaultValue = false)]
        public string Uri { get; set; }

        /// <summary>
        /// The REST URL of the definition.
        /// </summary>
        /// <value>The REST URL of the definition.</value>
        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildDefinition {\n");
            sb.Append("  BadgeEnabled: ").Append(BadgeEnabled).Append("\n");
            sb.Append("  BuildNumberFormat: ").Append(BuildNumberFormat).Append("\n");
            sb.Append("  Comment: ").Append(Comment).Append("\n");
            sb.Append("  Demands: ").Append(Demands).Append("\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  DropLocation: ").Append(DropLocation).Append("\n");
            sb.Append("  JobAuthorizationScope: ").Append(JobAuthorizationScope).Append("\n");
            sb.Append("  JobCancelTimeoutInMinutes: ").Append(JobCancelTimeoutInMinutes).Append("\n");
            sb.Append("  JobTimeoutInMinutes: ").Append(JobTimeoutInMinutes).Append("\n");
            sb.Append("  Options: ").Append(Options).Append("\n");
            sb.Append("  Process: ").Append(Process).Append("\n");
            sb.Append("  ProcessParameters: ").Append(ProcessParameters).Append("\n");
            sb.Append("  Properties: ").Append(Properties).Append("\n");
            sb.Append("  Repository: ").Append(Repository).Append("\n");
            sb.Append("  RetentionRules: ").Append(RetentionRules).Append("\n");
            sb.Append("  Tags: ").Append(Tags).Append("\n");
            sb.Append("  Triggers: ").Append(Triggers).Append("\n");
            sb.Append("  VariableGroups: ").Append(VariableGroups).Append("\n");
            sb.Append("  Variables: ").Append(Variables).Append("\n");
            sb.Append("  CreatedDate: ").Append(CreatedDate).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Path: ").Append(Path).Append("\n");
            sb.Append("  Project: ").Append(Project).Append("\n");
            sb.Append("  QueueStatus: ").Append(QueueStatus).Append("\n");
            sb.Append("  Revision: ").Append(Revision).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
            sb.Append("  Uri: ").Append(Uri).Append("\n");
            sb.Append("  Url: ").Append(Url).Append("\n");
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
            return this.Equals(input as BuildDefinition);
        }

        /// <summary>
        /// Returns true if BuildDefinition instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildDefinition to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildDefinition input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.BadgeEnabled == input.BadgeEnabled ||
                    this.BadgeEnabled.Equals(input.BadgeEnabled)
                ) && 
                (
                    this.BuildNumberFormat == input.BuildNumberFormat ||
                    (this.BuildNumberFormat != null &&
                    this.BuildNumberFormat.Equals(input.BuildNumberFormat))
                ) && 
                (
                    this.Comment == input.Comment ||
                    (this.Comment != null &&
                    this.Comment.Equals(input.Comment))
                ) && 
                (
                    this.Demands == input.Demands ||
                    this.Demands != null &&
                    input.Demands != null &&
                    this.Demands.SequenceEqual(input.Demands)
                ) && 
                (
                    this.Description == input.Description ||
                    (this.Description != null &&
                    this.Description.Equals(input.Description))
                ) && 
                (
                    this.DropLocation == input.DropLocation ||
                    (this.DropLocation != null &&
                    this.DropLocation.Equals(input.DropLocation))
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
                    this.Options == input.Options ||
                    this.Options != null &&
                    input.Options != null &&
                    this.Options.SequenceEqual(input.Options)
                ) && 
                (
                    this.Process == input.Process ||
                    (this.Process != null &&
                    this.Process.Equals(input.Process))
                ) && 
                (
                    this.ProcessParameters == input.ProcessParameters ||
                    (this.ProcessParameters != null &&
                    this.ProcessParameters.Equals(input.ProcessParameters))
                ) && 
                (
                    this.Properties == input.Properties ||
                    (this.Properties != null &&
                    this.Properties.Equals(input.Properties))
                ) && 
                (
                    this.Repository == input.Repository ||
                    (this.Repository != null &&
                    this.Repository.Equals(input.Repository))
                ) && 
                (
                    this.RetentionRules == input.RetentionRules ||
                    this.RetentionRules != null &&
                    input.RetentionRules != null &&
                    this.RetentionRules.SequenceEqual(input.RetentionRules)
                ) && 
                (
                    this.Tags == input.Tags ||
                    this.Tags != null &&
                    input.Tags != null &&
                    this.Tags.SequenceEqual(input.Tags)
                ) && 
                (
                    this.Triggers == input.Triggers ||
                    this.Triggers != null &&
                    input.Triggers != null &&
                    this.Triggers.SequenceEqual(input.Triggers)
                ) && 
                (
                    this.VariableGroups == input.VariableGroups ||
                    this.VariableGroups != null &&
                    input.VariableGroups != null &&
                    this.VariableGroups.SequenceEqual(input.VariableGroups)
                ) && 
                (
                    this.Variables == input.Variables ||
                    this.Variables != null &&
                    input.Variables != null &&
                    this.Variables.SequenceEqual(input.Variables)
                ) && 
                (
                    this.CreatedDate == input.CreatedDate ||
                    (this.CreatedDate != null &&
                    this.CreatedDate.Equals(input.CreatedDate))
                ) && 
                (
                    this.Id == input.Id ||
                    this.Id.Equals(input.Id)
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.Path == input.Path ||
                    (this.Path != null &&
                    this.Path.Equals(input.Path))
                ) && 
                (
                    this.Project == input.Project ||
                    (this.Project != null &&
                    this.Project.Equals(input.Project))
                ) && 
                (
                    this.QueueStatus == input.QueueStatus ||
                    this.QueueStatus.Equals(input.QueueStatus)
                ) && 
                (
                    this.Revision == input.Revision ||
                    this.Revision.Equals(input.Revision)
                ) && 
                (
                    this.Type == input.Type ||
                    this.Type.Equals(input.Type)
                ) && 
                (
                    this.Uri == input.Uri ||
                    (this.Uri != null &&
                    this.Uri.Equals(input.Uri))
                ) && 
                (
                    this.Url == input.Url ||
                    (this.Url != null &&
                    this.Url.Equals(input.Url))
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
                hashCode = (hashCode * 59) + this.BadgeEnabled.GetHashCode();
                if (this.BuildNumberFormat != null)
                {
                    hashCode = (hashCode * 59) + this.BuildNumberFormat.GetHashCode();
                }
                if (this.Comment != null)
                {
                    hashCode = (hashCode * 59) + this.Comment.GetHashCode();
                }
                if (this.Demands != null)
                {
                    hashCode = (hashCode * 59) + this.Demands.GetHashCode();
                }
                if (this.Description != null)
                {
                    hashCode = (hashCode * 59) + this.Description.GetHashCode();
                }
                if (this.DropLocation != null)
                {
                    hashCode = (hashCode * 59) + this.DropLocation.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.JobAuthorizationScope.GetHashCode();
                hashCode = (hashCode * 59) + this.JobCancelTimeoutInMinutes.GetHashCode();
                hashCode = (hashCode * 59) + this.JobTimeoutInMinutes.GetHashCode();
                if (this.Options != null)
                {
                    hashCode = (hashCode * 59) + this.Options.GetHashCode();
                }
                if (this.Process != null)
                {
                    hashCode = (hashCode * 59) + this.Process.GetHashCode();
                }
                if (this.ProcessParameters != null)
                {
                    hashCode = (hashCode * 59) + this.ProcessParameters.GetHashCode();
                }
                if (this.Properties != null)
                {
                    hashCode = (hashCode * 59) + this.Properties.GetHashCode();
                }
                if (this.Repository != null)
                {
                    hashCode = (hashCode * 59) + this.Repository.GetHashCode();
                }
                if (this.RetentionRules != null)
                {
                    hashCode = (hashCode * 59) + this.RetentionRules.GetHashCode();
                }
                if (this.Tags != null)
                {
                    hashCode = (hashCode * 59) + this.Tags.GetHashCode();
                }
                if (this.Triggers != null)
                {
                    hashCode = (hashCode * 59) + this.Triggers.GetHashCode();
                }
                if (this.VariableGroups != null)
                {
                    hashCode = (hashCode * 59) + this.VariableGroups.GetHashCode();
                }
                if (this.Variables != null)
                {
                    hashCode = (hashCode * 59) + this.Variables.GetHashCode();
                }
                if (this.CreatedDate != null)
                {
                    hashCode = (hashCode * 59) + this.CreatedDate.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Id.GetHashCode();
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                if (this.Path != null)
                {
                    hashCode = (hashCode * 59) + this.Path.GetHashCode();
                }
                if (this.Project != null)
                {
                    hashCode = (hashCode * 59) + this.Project.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.QueueStatus.GetHashCode();
                hashCode = (hashCode * 59) + this.Revision.GetHashCode();
                hashCode = (hashCode * 59) + this.Type.GetHashCode();
                if (this.Uri != null)
                {
                    hashCode = (hashCode * 59) + this.Uri.GetHashCode();
                }
                if (this.Url != null)
                {
                    hashCode = (hashCode * 59) + this.Url.GetHashCode();
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
