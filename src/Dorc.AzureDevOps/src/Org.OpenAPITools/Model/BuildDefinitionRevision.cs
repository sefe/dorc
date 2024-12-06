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
    /// Represents a revision of a build definition.
    /// </summary>
    [DataContract(Name = "BuildDefinitionRevision")]
    public partial class BuildDefinitionRevision : IEquatable<BuildDefinitionRevision>, IValidatableObject
    {
        /// <summary>
        /// The change type (add, edit, delete).
        /// </summary>
        /// <value>The change type (add, edit, delete).</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum ChangeTypeEnum
        {
            /// <summary>
            /// Enum Add for value: add
            /// </summary>
            [EnumMember(Value = "add")]
            Add = 1,

            /// <summary>
            /// Enum Update for value: update
            /// </summary>
            [EnumMember(Value = "update")]
            Update = 2,

            /// <summary>
            /// Enum Delete for value: delete
            /// </summary>
            [EnumMember(Value = "delete")]
            Delete = 3

        }


        /// <summary>
        /// The change type (add, edit, delete).
        /// </summary>
        /// <value>The change type (add, edit, delete).</value>
        [DataMember(Name = "changeType", EmitDefaultValue = false)]
        public ChangeTypeEnum? ChangeType { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildDefinitionRevision" /> class.
        /// </summary>
        /// <param name="changedBy">changedBy.</param>
        /// <param name="changedDate">The date and time that the definition was changed..</param>
        /// <param name="changeType">The change type (add, edit, delete)..</param>
        /// <param name="comment">The comment associated with the change..</param>
        /// <param name="definitionUrl">A link to the definition at this revision..</param>
        /// <param name="name">The name of the definition..</param>
        /// <param name="revision">The revision number..</param>
        public BuildDefinitionRevision(IdentityRef changedBy = default(IdentityRef), DateTime changedDate = default(DateTime), ChangeTypeEnum? changeType = default(ChangeTypeEnum?), string comment = default(string), string definitionUrl = default(string), string name = default(string), int revision = default(int))
        {
            this.ChangedBy = changedBy;
            this.ChangedDate = changedDate;
            this.ChangeType = changeType;
            this.Comment = comment;
            this.DefinitionUrl = definitionUrl;
            this.Name = name;
            this.Revision = revision;
        }

        /// <summary>
        /// Gets or Sets ChangedBy
        /// </summary>
        [DataMember(Name = "changedBy", EmitDefaultValue = false)]
        public IdentityRef ChangedBy { get; set; }

        /// <summary>
        /// The date and time that the definition was changed.
        /// </summary>
        /// <value>The date and time that the definition was changed.</value>
        [DataMember(Name = "changedDate", EmitDefaultValue = false)]
        public DateTime ChangedDate { get; set; }

        /// <summary>
        /// The comment associated with the change.
        /// </summary>
        /// <value>The comment associated with the change.</value>
        [DataMember(Name = "comment", EmitDefaultValue = false)]
        public string Comment { get; set; }

        /// <summary>
        /// A link to the definition at this revision.
        /// </summary>
        /// <value>A link to the definition at this revision.</value>
        [DataMember(Name = "definitionUrl", EmitDefaultValue = false)]
        public string DefinitionUrl { get; set; }

        /// <summary>
        /// The name of the definition.
        /// </summary>
        /// <value>The name of the definition.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// The revision number.
        /// </summary>
        /// <value>The revision number.</value>
        [DataMember(Name = "revision", EmitDefaultValue = false)]
        public int Revision { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildDefinitionRevision {\n");
            sb.Append("  ChangedBy: ").Append(ChangedBy).Append("\n");
            sb.Append("  ChangedDate: ").Append(ChangedDate).Append("\n");
            sb.Append("  ChangeType: ").Append(ChangeType).Append("\n");
            sb.Append("  Comment: ").Append(Comment).Append("\n");
            sb.Append("  DefinitionUrl: ").Append(DefinitionUrl).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Revision: ").Append(Revision).Append("\n");
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
            return this.Equals(input as BuildDefinitionRevision);
        }

        /// <summary>
        /// Returns true if BuildDefinitionRevision instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildDefinitionRevision to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildDefinitionRevision input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.ChangedBy == input.ChangedBy ||
                    (this.ChangedBy != null &&
                    this.ChangedBy.Equals(input.ChangedBy))
                ) && 
                (
                    this.ChangedDate == input.ChangedDate ||
                    (this.ChangedDate != null &&
                    this.ChangedDate.Equals(input.ChangedDate))
                ) && 
                (
                    this.ChangeType == input.ChangeType ||
                    this.ChangeType.Equals(input.ChangeType)
                ) && 
                (
                    this.Comment == input.Comment ||
                    (this.Comment != null &&
                    this.Comment.Equals(input.Comment))
                ) && 
                (
                    this.DefinitionUrl == input.DefinitionUrl ||
                    (this.DefinitionUrl != null &&
                    this.DefinitionUrl.Equals(input.DefinitionUrl))
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.Revision == input.Revision ||
                    this.Revision.Equals(input.Revision)
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
                if (this.ChangedBy != null)
                {
                    hashCode = (hashCode * 59) + this.ChangedBy.GetHashCode();
                }
                if (this.ChangedDate != null)
                {
                    hashCode = (hashCode * 59) + this.ChangedDate.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.ChangeType.GetHashCode();
                if (this.Comment != null)
                {
                    hashCode = (hashCode * 59) + this.Comment.GetHashCode();
                }
                if (this.DefinitionUrl != null)
                {
                    hashCode = (hashCode * 59) + this.DefinitionUrl.GetHashCode();
                }
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Revision.GetHashCode();
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
