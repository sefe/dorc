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
    /// Represents a reference to an agent pool.
    /// </summary>
    [DataContract(Name = "TaskAgentPoolReference")]
    public partial class TaskAgentPoolReference : IEquatable<TaskAgentPoolReference>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TaskAgentPoolReference" /> class.
        /// </summary>
        /// <param name="id">The pool ID..</param>
        /// <param name="isHosted">A value indicating whether or not this pool is managed by the service..</param>
        /// <param name="name">The pool name..</param>
        public TaskAgentPoolReference(int id = default(int), bool isHosted = default(bool), string name = default(string))
        {
            this.Id = id;
            this.IsHosted = isHosted;
            this.Name = name;
        }

        /// <summary>
        /// The pool ID.
        /// </summary>
        /// <value>The pool ID.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public int Id { get; set; }

        /// <summary>
        /// A value indicating whether or not this pool is managed by the service.
        /// </summary>
        /// <value>A value indicating whether or not this pool is managed by the service.</value>
        [DataMember(Name = "isHosted", EmitDefaultValue = true)]
        public bool IsHosted { get; set; }

        /// <summary>
        /// The pool name.
        /// </summary>
        /// <value>The pool name.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class TaskAgentPoolReference {\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  IsHosted: ").Append(IsHosted).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
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
            return this.Equals(input as TaskAgentPoolReference);
        }

        /// <summary>
        /// Returns true if TaskAgentPoolReference instances are equal
        /// </summary>
        /// <param name="input">Instance of TaskAgentPoolReference to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(TaskAgentPoolReference input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Id == input.Id ||
                    this.Id.Equals(input.Id)
                ) && 
                (
                    this.IsHosted == input.IsHosted ||
                    this.IsHosted.Equals(input.IsHosted)
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
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
                hashCode = (hashCode * 59) + this.Id.GetHashCode();
                hashCode = (hashCode * 59) + this.IsHosted.GetHashCode();
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
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
