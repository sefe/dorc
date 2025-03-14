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
    /// BuildTagsAddedEvent
    /// </summary>
    [DataContract(Name = "BuildTagsAddedEvent")]
    public partial class BuildTagsAddedEvent : IEquatable<BuildTagsAddedEvent>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildTagsAddedEvent" /> class.
        /// </summary>
        /// <param name="allTags">allTags.</param>
        /// <param name="newTags">newTags.</param>
        /// <param name="buildId">buildId.</param>
        public BuildTagsAddedEvent(List<string> allTags = default(List<string>), List<string> newTags = default(List<string>), int buildId = default(int))
        {
            this.AllTags = allTags;
            this.NewTags = newTags;
            this.BuildId = buildId;
        }

        /// <summary>
        /// Gets or Sets AllTags
        /// </summary>
        [DataMember(Name = "allTags", EmitDefaultValue = false)]
        public List<string> AllTags { get; set; }

        /// <summary>
        /// Gets or Sets NewTags
        /// </summary>
        [DataMember(Name = "newTags", EmitDefaultValue = false)]
        public List<string> NewTags { get; set; }

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
            sb.Append("class BuildTagsAddedEvent {\n");
            sb.Append("  AllTags: ").Append(AllTags).Append("\n");
            sb.Append("  NewTags: ").Append(NewTags).Append("\n");
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
            return this.Equals(input as BuildTagsAddedEvent);
        }

        /// <summary>
        /// Returns true if BuildTagsAddedEvent instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildTagsAddedEvent to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildTagsAddedEvent input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.AllTags == input.AllTags ||
                    this.AllTags != null &&
                    input.AllTags != null &&
                    this.AllTags.SequenceEqual(input.AllTags)
                ) && 
                (
                    this.NewTags == input.NewTags ||
                    this.NewTags != null &&
                    input.NewTags != null &&
                    this.NewTags.SequenceEqual(input.NewTags)
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
                if (this.AllTags != null)
                {
                    hashCode = (hashCode * 59) + this.AllTags.GetHashCode();
                }
                if (this.NewTags != null)
                {
                    hashCode = (hashCode * 59) + this.NewTags.GetHashCode();
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
