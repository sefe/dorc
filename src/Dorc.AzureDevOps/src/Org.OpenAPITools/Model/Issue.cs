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
    /// Represents an issue (error, warning) associated with a build.
    /// </summary>
    [DataContract(Name = "Issue")]
    public partial class Issue : IEquatable<Issue>, IValidatableObject
    {
        /// <summary>
        /// The type (error, warning) of the issue.
        /// </summary>
        /// <value>The type (error, warning) of the issue.</value>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum TypeEnum
        {
            /// <summary>
            /// Enum Error for value: error
            /// </summary>
            [EnumMember(Value = "error")]
            Error = 1,

            /// <summary>
            /// Enum Warning for value: warning
            /// </summary>
            [EnumMember(Value = "warning")]
            Warning = 2

        }


        /// <summary>
        /// The type (error, warning) of the issue.
        /// </summary>
        /// <value>The type (error, warning) of the issue.</value>
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public TypeEnum? Type { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="Issue" /> class.
        /// </summary>
        /// <param name="category">The category..</param>
        /// <param name="data">data.</param>
        /// <param name="message">A description of the issue..</param>
        /// <param name="type">The type (error, warning) of the issue..</param>
        public Issue(string category = default(string), Dictionary<string, string> data = default(Dictionary<string, string>), string message = default(string), TypeEnum? type = default(TypeEnum?))
        {
            this.Category = category;
            this.Data = data;
            this.Message = message;
            this.Type = type;
        }

        /// <summary>
        /// The category.
        /// </summary>
        /// <value>The category.</value>
        [DataMember(Name = "category", EmitDefaultValue = false)]
        public string Category { get; set; }

        /// <summary>
        /// Gets or Sets Data
        /// </summary>
        [DataMember(Name = "data", EmitDefaultValue = false)]
        public Dictionary<string, string> Data { get; set; }

        /// <summary>
        /// A description of the issue.
        /// </summary>
        /// <value>A description of the issue.</value>
        [DataMember(Name = "message", EmitDefaultValue = false)]
        public string Message { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class Issue {\n");
            sb.Append("  Category: ").Append(Category).Append("\n");
            sb.Append("  Data: ").Append(Data).Append("\n");
            sb.Append("  Message: ").Append(Message).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
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
            return this.Equals(input as Issue);
        }

        /// <summary>
        /// Returns true if Issue instances are equal
        /// </summary>
        /// <param name="input">Instance of Issue to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(Issue input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Category == input.Category ||
                    (this.Category != null &&
                    this.Category.Equals(input.Category))
                ) && 
                (
                    this.Data == input.Data ||
                    this.Data != null &&
                    input.Data != null &&
                    this.Data.SequenceEqual(input.Data)
                ) && 
                (
                    this.Message == input.Message ||
                    (this.Message != null &&
                    this.Message.Equals(input.Message))
                ) && 
                (
                    this.Type == input.Type ||
                    this.Type.Equals(input.Type)
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
                if (this.Category != null)
                {
                    hashCode = (hashCode * 59) + this.Category.GetHashCode();
                }
                if (this.Data != null)
                {
                    hashCode = (hashCode * 59) + this.Data.GetHashCode();
                }
                if (this.Message != null)
                {
                    hashCode = (hashCode * 59) + this.Message.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Type.GetHashCode();
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
