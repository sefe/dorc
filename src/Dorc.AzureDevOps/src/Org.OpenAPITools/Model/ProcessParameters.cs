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
    /// ProcessParameters
    /// </summary>
    [DataContract(Name = "ProcessParameters")]
    public partial class ProcessParameters : IEquatable<ProcessParameters>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessParameters" /> class.
        /// </summary>
        /// <param name="dataSourceBindings">dataSourceBindings.</param>
        /// <param name="inputs">inputs.</param>
        /// <param name="sourceDefinitions">sourceDefinitions.</param>
        public ProcessParameters(List<DataSourceBindingBase> dataSourceBindings = default(List<DataSourceBindingBase>), List<TaskInputDefinitionBase> inputs = default(List<TaskInputDefinitionBase>), List<TaskSourceDefinitionBase> sourceDefinitions = default(List<TaskSourceDefinitionBase>))
        {
            this.DataSourceBindings = dataSourceBindings;
            this.Inputs = inputs;
            this.SourceDefinitions = sourceDefinitions;
        }

        /// <summary>
        /// Gets or Sets DataSourceBindings
        /// </summary>
        [DataMember(Name = "dataSourceBindings", EmitDefaultValue = false)]
        public List<DataSourceBindingBase> DataSourceBindings { get; set; }

        /// <summary>
        /// Gets or Sets Inputs
        /// </summary>
        [DataMember(Name = "inputs", EmitDefaultValue = false)]
        public List<TaskInputDefinitionBase> Inputs { get; set; }

        /// <summary>
        /// Gets or Sets SourceDefinitions
        /// </summary>
        [DataMember(Name = "sourceDefinitions", EmitDefaultValue = false)]
        public List<TaskSourceDefinitionBase> SourceDefinitions { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ProcessParameters {\n");
            sb.Append("  DataSourceBindings: ").Append(DataSourceBindings).Append("\n");
            sb.Append("  Inputs: ").Append(Inputs).Append("\n");
            sb.Append("  SourceDefinitions: ").Append(SourceDefinitions).Append("\n");
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
            return this.Equals(input as ProcessParameters);
        }

        /// <summary>
        /// Returns true if ProcessParameters instances are equal
        /// </summary>
        /// <param name="input">Instance of ProcessParameters to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(ProcessParameters input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.DataSourceBindings == input.DataSourceBindings ||
                    this.DataSourceBindings != null &&
                    input.DataSourceBindings != null &&
                    this.DataSourceBindings.SequenceEqual(input.DataSourceBindings)
                ) && 
                (
                    this.Inputs == input.Inputs ||
                    this.Inputs != null &&
                    input.Inputs != null &&
                    this.Inputs.SequenceEqual(input.Inputs)
                ) && 
                (
                    this.SourceDefinitions == input.SourceDefinitions ||
                    this.SourceDefinitions != null &&
                    input.SourceDefinitions != null &&
                    this.SourceDefinitions.SequenceEqual(input.SourceDefinitions)
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
                if (this.DataSourceBindings != null)
                {
                    hashCode = (hashCode * 59) + this.DataSourceBindings.GetHashCode();
                }
                if (this.Inputs != null)
                {
                    hashCode = (hashCode * 59) + this.Inputs.GetHashCode();
                }
                if (this.SourceDefinitions != null)
                {
                    hashCode = (hashCode * 59) + this.SourceDefinitions.GetHashCode();
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
