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
    /// Represents a folder that contains build definitions.
    /// </summary>
    [DataContract(Name = "Folder")]
    public partial class Folder : IEquatable<Folder>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Folder" /> class.
        /// </summary>
        /// <param name="createdBy">createdBy.</param>
        /// <param name="createdOn">The date the folder was created..</param>
        /// <param name="description">The description..</param>
        /// <param name="lastChangedBy">lastChangedBy.</param>
        /// <param name="lastChangedDate">The date the folder was last changed..</param>
        /// <param name="path">The full path..</param>
        /// <param name="project">project.</param>
        public Folder(IdentityRef createdBy = default(IdentityRef), DateTime createdOn = default(DateTime), string description = default(string), IdentityRef lastChangedBy = default(IdentityRef), DateTime lastChangedDate = default(DateTime), string path = default(string), TeamProjectReference project = default(TeamProjectReference))
        {
            this.CreatedBy = createdBy;
            this.CreatedOn = createdOn;
            this.Description = description;
            this.LastChangedBy = lastChangedBy;
            this.LastChangedDate = lastChangedDate;
            this.Path = path;
            this.Project = project;
        }

        /// <summary>
        /// Gets or Sets CreatedBy
        /// </summary>
        [DataMember(Name = "createdBy", EmitDefaultValue = false)]
        public IdentityRef CreatedBy { get; set; }

        /// <summary>
        /// The date the folder was created.
        /// </summary>
        /// <value>The date the folder was created.</value>
        [DataMember(Name = "createdOn", EmitDefaultValue = false)]
        public DateTime CreatedOn { get; set; }

        /// <summary>
        /// The description.
        /// </summary>
        /// <value>The description.</value>
        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string Description { get; set; }

        /// <summary>
        /// Gets or Sets LastChangedBy
        /// </summary>
        [DataMember(Name = "lastChangedBy", EmitDefaultValue = false)]
        public IdentityRef LastChangedBy { get; set; }

        /// <summary>
        /// The date the folder was last changed.
        /// </summary>
        /// <value>The date the folder was last changed.</value>
        [DataMember(Name = "lastChangedDate", EmitDefaultValue = false)]
        public DateTime LastChangedDate { get; set; }

        /// <summary>
        /// The full path.
        /// </summary>
        /// <value>The full path.</value>
        [DataMember(Name = "path", EmitDefaultValue = false)]
        public string Path { get; set; }

        /// <summary>
        /// Gets or Sets Project
        /// </summary>
        [DataMember(Name = "project", EmitDefaultValue = false)]
        public TeamProjectReference Project { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class Folder {\n");
            sb.Append("  CreatedBy: ").Append(CreatedBy).Append("\n");
            sb.Append("  CreatedOn: ").Append(CreatedOn).Append("\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  LastChangedBy: ").Append(LastChangedBy).Append("\n");
            sb.Append("  LastChangedDate: ").Append(LastChangedDate).Append("\n");
            sb.Append("  Path: ").Append(Path).Append("\n");
            sb.Append("  Project: ").Append(Project).Append("\n");
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
            return this.Equals(input as Folder);
        }

        /// <summary>
        /// Returns true if Folder instances are equal
        /// </summary>
        /// <param name="input">Instance of Folder to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(Folder input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.CreatedBy == input.CreatedBy ||
                    (this.CreatedBy != null &&
                    this.CreatedBy.Equals(input.CreatedBy))
                ) && 
                (
                    this.CreatedOn == input.CreatedOn ||
                    (this.CreatedOn != null &&
                    this.CreatedOn.Equals(input.CreatedOn))
                ) && 
                (
                    this.Description == input.Description ||
                    (this.Description != null &&
                    this.Description.Equals(input.Description))
                ) && 
                (
                    this.LastChangedBy == input.LastChangedBy ||
                    (this.LastChangedBy != null &&
                    this.LastChangedBy.Equals(input.LastChangedBy))
                ) && 
                (
                    this.LastChangedDate == input.LastChangedDate ||
                    (this.LastChangedDate != null &&
                    this.LastChangedDate.Equals(input.LastChangedDate))
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
                if (this.CreatedBy != null)
                {
                    hashCode = (hashCode * 59) + this.CreatedBy.GetHashCode();
                }
                if (this.CreatedOn != null)
                {
                    hashCode = (hashCode * 59) + this.CreatedOn.GetHashCode();
                }
                if (this.Description != null)
                {
                    hashCode = (hashCode * 59) + this.Description.GetHashCode();
                }
                if (this.LastChangedBy != null)
                {
                    hashCode = (hashCode * 59) + this.LastChangedBy.GetHashCode();
                }
                if (this.LastChangedDate != null)
                {
                    hashCode = (hashCode * 59) + this.LastChangedDate.GetHashCode();
                }
                if (this.Path != null)
                {
                    hashCode = (hashCode * 59) + this.Path.GetHashCode();
                }
                if (this.Project != null)
                {
                    hashCode = (hashCode * 59) + this.Project.GetHashCode();
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
