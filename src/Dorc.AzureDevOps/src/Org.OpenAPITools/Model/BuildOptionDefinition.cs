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
    /// Represents an optional behavior that can be applied to a build definition.
    /// </summary>
    [DataContract(Name = "BuildOptionDefinition")]
    public partial class BuildOptionDefinition : IEquatable<BuildOptionDefinition>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildOptionDefinition" /> class.
        /// </summary>
        /// <param name="description">The description..</param>
        /// <param name="groups">The list of input groups defined for the build option..</param>
        /// <param name="inputs">The list of inputs defined for the build option..</param>
        /// <param name="name">The name of the build option..</param>
        /// <param name="ordinal">A value that indicates the relative order in which the behavior should be applied..</param>
        /// <param name="id">The ID of the referenced build option..</param>
        public BuildOptionDefinition(string description = default(string), List<BuildOptionGroupDefinition> groups = default(List<BuildOptionGroupDefinition>), List<BuildOptionInputDefinition> inputs = default(List<BuildOptionInputDefinition>), string name = default(string), int ordinal = default(int), Guid id = default(Guid))
        {
            this.Description = description;
            this.Groups = groups;
            this.Inputs = inputs;
            this.Name = name;
            this.Ordinal = ordinal;
            this.Id = id;
        }

        /// <summary>
        /// The description.
        /// </summary>
        /// <value>The description.</value>
        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string Description { get; set; }

        /// <summary>
        /// The list of input groups defined for the build option.
        /// </summary>
        /// <value>The list of input groups defined for the build option.</value>
        [DataMember(Name = "groups", EmitDefaultValue = false)]
        public List<BuildOptionGroupDefinition> Groups { get; set; }

        /// <summary>
        /// The list of inputs defined for the build option.
        /// </summary>
        /// <value>The list of inputs defined for the build option.</value>
        [DataMember(Name = "inputs", EmitDefaultValue = false)]
        public List<BuildOptionInputDefinition> Inputs { get; set; }

        /// <summary>
        /// The name of the build option.
        /// </summary>
        /// <value>The name of the build option.</value>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// A value that indicates the relative order in which the behavior should be applied.
        /// </summary>
        /// <value>A value that indicates the relative order in which the behavior should be applied.</value>
        [DataMember(Name = "ordinal", EmitDefaultValue = false)]
        public int Ordinal { get; set; }

        /// <summary>
        /// The ID of the referenced build option.
        /// </summary>
        /// <value>The ID of the referenced build option.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public Guid Id { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildOptionDefinition {\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  Groups: ").Append(Groups).Append("\n");
            sb.Append("  Inputs: ").Append(Inputs).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Ordinal: ").Append(Ordinal).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
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
            return this.Equals(input as BuildOptionDefinition);
        }

        /// <summary>
        /// Returns true if BuildOptionDefinition instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildOptionDefinition to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildOptionDefinition input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Description == input.Description ||
                    (this.Description != null &&
                    this.Description.Equals(input.Description))
                ) && 
                (
                    this.Groups == input.Groups ||
                    this.Groups != null &&
                    input.Groups != null &&
                    this.Groups.SequenceEqual(input.Groups)
                ) && 
                (
                    this.Inputs == input.Inputs ||
                    this.Inputs != null &&
                    input.Inputs != null &&
                    this.Inputs.SequenceEqual(input.Inputs)
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.Ordinal == input.Ordinal ||
                    this.Ordinal.Equals(input.Ordinal)
                ) && 
                (
                    this.Id == input.Id ||
                    (this.Id != null &&
                    this.Id.Equals(input.Id))
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
                if (this.Description != null)
                {
                    hashCode = (hashCode * 59) + this.Description.GetHashCode();
                }
                if (this.Groups != null)
                {
                    hashCode = (hashCode * 59) + this.Groups.GetHashCode();
                }
                if (this.Inputs != null)
                {
                    hashCode = (hashCode * 59) + this.Inputs.GetHashCode();
                }
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Ordinal.GetHashCode();
                if (this.Id != null)
                {
                    hashCode = (hashCode * 59) + this.Id.GetHashCode();
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
