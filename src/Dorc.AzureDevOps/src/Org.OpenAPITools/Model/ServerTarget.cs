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
    /// Represents a phase target that runs on the server.
    /// </summary>
    [DataContract(Name = "ServerTarget")]
    public partial class ServerTarget : IEquatable<ServerTarget>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerTarget" /> class.
        /// </summary>
        /// <param name="executionOptions">executionOptions.</param>
        /// <param name="type">The type of the target..</param>
        public ServerTarget(ServerTargetExecutionOptions executionOptions = default(ServerTargetExecutionOptions), int type = default(int))
        {
            this.ExecutionOptions = executionOptions;
            this.Type = type;
        }

        /// <summary>
        /// Gets or Sets ExecutionOptions
        /// </summary>
        [DataMember(Name = "executionOptions", EmitDefaultValue = false)]
        public ServerTargetExecutionOptions ExecutionOptions { get; set; }

        /// <summary>
        /// The type of the target.
        /// </summary>
        /// <value>The type of the target.</value>
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public int Type { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ServerTarget {\n");
            sb.Append("  ExecutionOptions: ").Append(ExecutionOptions).Append("\n");
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
            return this.Equals(input as ServerTarget);
        }

        /// <summary>
        /// Returns true if ServerTarget instances are equal
        /// </summary>
        /// <param name="input">Instance of ServerTarget to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(ServerTarget input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.ExecutionOptions == input.ExecutionOptions ||
                    (this.ExecutionOptions != null &&
                    this.ExecutionOptions.Equals(input.ExecutionOptions))
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
                if (this.ExecutionOptions != null)
                {
                    hashCode = (hashCode * 59) + this.ExecutionOptions.GetHashCode();
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
