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
    /// Represents options for running a phase based on values specified by a list of variables.
    /// </summary>
    [DataContract(Name = "VariableMultipliersServerExecutionOptions")]
    public partial class VariableMultipliersServerExecutionOptions : IEquatable<VariableMultipliersServerExecutionOptions>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VariableMultipliersServerExecutionOptions" /> class.
        /// </summary>
        /// <param name="continueOnError">Indicates whether failure of one job should prevent the phase from running in other jobs..</param>
        /// <param name="maxConcurrency">The maximum number of server jobs to run in parallel..</param>
        /// <param name="multipliers">multipliers.</param>
        /// <param name="type">The type..</param>
        public VariableMultipliersServerExecutionOptions(bool continueOnError = default(bool), int maxConcurrency = default(int), List<string> multipliers = default(List<string>), int type = default(int))
        {
            this.ContinueOnError = continueOnError;
            this.MaxConcurrency = maxConcurrency;
            this.Multipliers = multipliers;
            this.Type = type;
        }

        /// <summary>
        /// Indicates whether failure of one job should prevent the phase from running in other jobs.
        /// </summary>
        /// <value>Indicates whether failure of one job should prevent the phase from running in other jobs.</value>
        [DataMember(Name = "continueOnError", EmitDefaultValue = true)]
        public bool ContinueOnError { get; set; }

        /// <summary>
        /// The maximum number of server jobs to run in parallel.
        /// </summary>
        /// <value>The maximum number of server jobs to run in parallel.</value>
        [DataMember(Name = "maxConcurrency", EmitDefaultValue = false)]
        public int MaxConcurrency { get; set; }

        /// <summary>
        /// Gets or Sets Multipliers
        /// </summary>
        [DataMember(Name = "multipliers", EmitDefaultValue = false)]
        public List<string> Multipliers { get; set; }

        /// <summary>
        /// The type.
        /// </summary>
        /// <value>The type.</value>
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public int Type { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class VariableMultipliersServerExecutionOptions {\n");
            sb.Append("  ContinueOnError: ").Append(ContinueOnError).Append("\n");
            sb.Append("  MaxConcurrency: ").Append(MaxConcurrency).Append("\n");
            sb.Append("  Multipliers: ").Append(Multipliers).Append("\n");
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
            return this.Equals(input as VariableMultipliersServerExecutionOptions);
        }

        /// <summary>
        /// Returns true if VariableMultipliersServerExecutionOptions instances are equal
        /// </summary>
        /// <param name="input">Instance of VariableMultipliersServerExecutionOptions to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(VariableMultipliersServerExecutionOptions input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.ContinueOnError == input.ContinueOnError ||
                    this.ContinueOnError.Equals(input.ContinueOnError)
                ) && 
                (
                    this.MaxConcurrency == input.MaxConcurrency ||
                    this.MaxConcurrency.Equals(input.MaxConcurrency)
                ) && 
                (
                    this.Multipliers == input.Multipliers ||
                    this.Multipliers != null &&
                    input.Multipliers != null &&
                    this.Multipliers.SequenceEqual(input.Multipliers)
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
                hashCode = (hashCode * 59) + this.ContinueOnError.GetHashCode();
                hashCode = (hashCode * 59) + this.MaxConcurrency.GetHashCode();
                if (this.Multipliers != null)
                {
                    hashCode = (hashCode * 59) + this.Multipliers.GetHashCode();
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
