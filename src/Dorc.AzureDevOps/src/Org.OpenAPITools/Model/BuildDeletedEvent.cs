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
    /// BuildDeletedEvent
    /// </summary>
    [DataContract(Name = "BuildDeletedEvent")]
    public partial class BuildDeletedEvent : IEquatable<BuildDeletedEvent>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildDeletedEvent" /> class.
        /// </summary>
        /// <param name="build">build.</param>
        /// <param name="buildId">buildId.</param>
        public BuildDeletedEvent(Build build = default(Build), int buildId = default(int))
        {
            this.Build = build;
            this.BuildId = buildId;
        }

        /// <summary>
        /// Gets or Sets Build
        /// </summary>
        [DataMember(Name = "build", EmitDefaultValue = false)]
        public Build Build { get; set; }

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
            sb.Append("class BuildDeletedEvent {\n");
            sb.Append("  Build: ").Append(Build).Append("\n");
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
            return this.Equals(input as BuildDeletedEvent);
        }

        /// <summary>
        /// Returns true if BuildDeletedEvent instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildDeletedEvent to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildDeletedEvent input)
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
                if (this.Build != null)
                {
                    hashCode = (hashCode * 59) + this.Build.GetHashCode();
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
