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
    /// Represents a variable used by a build definition.
    /// </summary>
    [DataContract(Name = "BuildDefinitionVariable")]
    public partial class BuildDefinitionVariable : IEquatable<BuildDefinitionVariable>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildDefinitionVariable" /> class.
        /// </summary>
        /// <param name="allowOverride">Indicates whether the value can be set at queue time..</param>
        /// <param name="isSecret">Indicates whether the variable&#39;s value is a secret..</param>
        /// <param name="value">The value of the variable..</param>
        public BuildDefinitionVariable(bool allowOverride = default(bool), bool isSecret = default(bool), string value = default(string))
        {
            this.AllowOverride = allowOverride;
            this.IsSecret = isSecret;
            this.Value = value;
        }

        /// <summary>
        /// Indicates whether the value can be set at queue time.
        /// </summary>
        /// <value>Indicates whether the value can be set at queue time.</value>
        [DataMember(Name = "allowOverride", EmitDefaultValue = true)]
        public bool AllowOverride { get; set; }

        /// <summary>
        /// Indicates whether the variable&#39;s value is a secret.
        /// </summary>
        /// <value>Indicates whether the variable&#39;s value is a secret.</value>
        [DataMember(Name = "isSecret", EmitDefaultValue = true)]
        public bool IsSecret { get; set; }

        /// <summary>
        /// The value of the variable.
        /// </summary>
        /// <value>The value of the variable.</value>
        [DataMember(Name = "value", EmitDefaultValue = false)]
        public string Value { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildDefinitionVariable {\n");
            sb.Append("  AllowOverride: ").Append(AllowOverride).Append("\n");
            sb.Append("  IsSecret: ").Append(IsSecret).Append("\n");
            sb.Append("  Value: ").Append(Value).Append("\n");
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
            return this.Equals(input as BuildDefinitionVariable);
        }

        /// <summary>
        /// Returns true if BuildDefinitionVariable instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildDefinitionVariable to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildDefinitionVariable input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.AllowOverride == input.AllowOverride ||
                    this.AllowOverride.Equals(input.AllowOverride)
                ) && 
                (
                    this.IsSecret == input.IsSecret ||
                    this.IsSecret.Equals(input.IsSecret)
                ) && 
                (
                    this.Value == input.Value ||
                    (this.Value != null &&
                    this.Value.Equals(input.Value))
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
                hashCode = (hashCode * 59) + this.AllowOverride.GetHashCode();
                hashCode = (hashCode * 59) + this.IsSecret.GetHashCode();
                if (this.Value != null)
                {
                    hashCode = (hashCode * 59) + this.Value.GetHashCode();
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
