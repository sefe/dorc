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
    /// Represents a shallow reference to a TeamProject.
    /// </summary>
    [DataContract(Name = "TeamProjectReference")]
    public partial class TeamProjectReference : IEquatable<TeamProjectReference>, IValidatableObject
    {
        /// <summary>
        /// Project state.
        /// </summary>
        /// <value>Project state.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum StateEnum
        {
            /// <summary>
            /// Enum Deleting for value: deleting
            /// </summary>
            [EnumMember(Value = "deleting")]
            Deleting = 1,

            /// <summary>
            /// Enum New for value: new
            /// </summary>
            [EnumMember(Value = "new")]
            New = 2,

            /// <summary>
            /// Enum WellFormed for value: wellFormed
            /// </summary>
            [EnumMember(Value = "wellFormed")]
            WellFormed = 3,

            /// <summary>
            /// Enum CreatePending for value: createPending
            /// </summary>
            [EnumMember(Value = "createPending")]
            CreatePending = 4,

            /// <summary>
            /// Enum All for value: all
            /// </summary>
            [EnumMember(Value = "all")]
            All = 5,

            /// <summary>
            /// Enum Unchanged for value: unchanged
            /// </summary>
            [EnumMember(Value = "unchanged")]
            Unchanged = 6,

            /// <summary>
            /// Enum Deleted for value: deleted
            /// </summary>
            [EnumMember(Value = "deleted")]
            Deleted = 7

        }


        /// <summary>
        /// Project state.
        /// </summary>
        /// <value>Project state.</value>
        [DataMember(Name = "state", EmitDefaultValue = false)]
        public StateEnum? State { get; set; }
        /// <summary>
        /// Project visibility.
        /// </summary>
        /// <value>Project visibility.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum VisibilityEnum
        {
            /// <summary>
            /// Enum Private for value: private
            /// </summary>
            [EnumMember(Value = "private")]
            Private = 1,

            /// <summary>
            /// Enum Public for value: public
            /// </summary>
            [EnumMember(Value = "public")]
            Public = 2

        }


        /// <summary>
        /// Project visibility.
        /// </summary>
        /// <value>Project visibility.</value>
        [DataMember(Name = "visibility", EmitDefaultValue = false)]
        public VisibilityEnum? Visibility { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="TeamProjectReference" /> class.
        /// </summary>
        /// <param name="abbreviation">Project abbreviation..</param>
        /// <param name="defaultTeamImageUrl">Url to default team identity image..</param>
        /// <param name="description">The project&#39;s description (if any)..</param>
        /// <param name="id">Project identifier..</param>
        /// <param name="lastUpdateTime">Project last update time..</param>
        /// <param name="name">Project name..</param>
        /// <param name="revision">Project revision..</param>
        /// <param name="state">Project state..</param>
        /// <param name="url">Url to the full version of the object..</param>
        /// <param name="visibility">Project visibility..</param>
        public TeamProjectReference(string abbreviation = default(string), string defaultTeamImageUrl = default(string), string description = default(string), Guid id = default(Guid), DateTime lastUpdateTime = default(DateTime), string name = default(string), long revision = default(long), StateEnum? state = default(StateEnum?), string url = default(string), VisibilityEnum? visibility = default(VisibilityEnum?))
        {
            this.Abbreviation = abbreviation;
            this.DefaultTeamImageUrl = defaultTeamImageUrl;
            this.Description = description;
            this.Id = id;
            this.LastUpdateTime = lastUpdateTime;
            this.Name = name;
            this.Revision = revision;
            this.State = state;
            this.Url = url;
            this.Visibility = visibility;
        }

        /// <summary>
        /// Project abbreviation.
        /// </summary>
        /// <value>Project abbreviation.</value>
        [DataMember(Name = "abbreviation", EmitDefaultValue = false)]
        public string Abbreviation { get; set; }

        /// <summary>
        /// Url to default team identity image.
        /// </summary>
        /// <value>Url to default team identity image.</value>
        [DataMember(Name = "defaultTeamImageUrl", EmitDefaultValue = false)]
        public string DefaultTeamImageUrl { get; set; }

        /// <summary>
        /// The project&#39;s description (if any).
        /// </summary>
        /// <value>The project&#39;s description (if any).</value>
        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string Description { get; set; }

        /// <summary>
        /// Project identifier.
        /// </summary>
        /// <value>Project identifier.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public Guid Id { get; set; }

        /// <summary>
        /// Project last update time.
        /// </summary>
        /// <value>Project last update time.</value>
        [DataMember(Name = "lastUpdateTime", EmitDefaultValue = false)]
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// Project name.
        /// </summary>
        /// <value>Project name.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// Project revision.
        /// </summary>
        /// <value>Project revision.</value>
        [DataMember(Name = "revision", EmitDefaultValue = false)]
        public long Revision { get; set; }

        /// <summary>
        /// Url to the full version of the object.
        /// </summary>
        /// <value>Url to the full version of the object.</value>
        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class TeamProjectReference {\n");
            sb.Append("  Abbreviation: ").Append(Abbreviation).Append("\n");
            sb.Append("  DefaultTeamImageUrl: ").Append(DefaultTeamImageUrl).Append("\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  LastUpdateTime: ").Append(LastUpdateTime).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Revision: ").Append(Revision).Append("\n");
            sb.Append("  State: ").Append(State).Append("\n");
            sb.Append("  Url: ").Append(Url).Append("\n");
            sb.Append("  Visibility: ").Append(Visibility).Append("\n");
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
            return this.Equals(input as TeamProjectReference);
        }

        /// <summary>
        /// Returns true if TeamProjectReference instances are equal
        /// </summary>
        /// <param name="input">Instance of TeamProjectReference to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(TeamProjectReference input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Abbreviation == input.Abbreviation ||
                    (this.Abbreviation != null &&
                    this.Abbreviation.Equals(input.Abbreviation))
                ) && 
                (
                    this.DefaultTeamImageUrl == input.DefaultTeamImageUrl ||
                    (this.DefaultTeamImageUrl != null &&
                    this.DefaultTeamImageUrl.Equals(input.DefaultTeamImageUrl))
                ) && 
                (
                    this.Description == input.Description ||
                    (this.Description != null &&
                    this.Description.Equals(input.Description))
                ) && 
                (
                    this.Id == input.Id ||
                    (this.Id != null &&
                    this.Id.Equals(input.Id))
                ) && 
                (
                    this.LastUpdateTime == input.LastUpdateTime ||
                    (this.LastUpdateTime != null &&
                    this.LastUpdateTime.Equals(input.LastUpdateTime))
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.Revision == input.Revision ||
                    this.Revision.Equals(input.Revision)
                ) && 
                (
                    this.State == input.State ||
                    this.State.Equals(input.State)
                ) && 
                (
                    this.Url == input.Url ||
                    (this.Url != null &&
                    this.Url.Equals(input.Url))
                ) && 
                (
                    this.Visibility == input.Visibility ||
                    this.Visibility.Equals(input.Visibility)
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
                if (this.Abbreviation != null)
                {
                    hashCode = (hashCode * 59) + this.Abbreviation.GetHashCode();
                }
                if (this.DefaultTeamImageUrl != null)
                {
                    hashCode = (hashCode * 59) + this.DefaultTeamImageUrl.GetHashCode();
                }
                if (this.Description != null)
                {
                    hashCode = (hashCode * 59) + this.Description.GetHashCode();
                }
                if (this.Id != null)
                {
                    hashCode = (hashCode * 59) + this.Id.GetHashCode();
                }
                if (this.LastUpdateTime != null)
                {
                    hashCode = (hashCode * 59) + this.LastUpdateTime.GetHashCode();
                }
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Revision.GetHashCode();
                hashCode = (hashCode * 59) + this.State.GetHashCode();
                if (this.Url != null)
                {
                    hashCode = (hashCode * 59) + this.Url.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Visibility.GetHashCode();
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
