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
    /// MinimalRetentionLease
    /// </summary>
    [DataContract(Name = "MinimalRetentionLease")]
    public partial class MinimalRetentionLease : IEquatable<MinimalRetentionLease>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MinimalRetentionLease" /> class.
        /// </summary>
        /// <param name="definitionId">The pipeline definition of the run..</param>
        /// <param name="ownerId">User-provided string that identifies the owner of a retention lease..</param>
        /// <param name="runId">The pipeline run to protect..</param>
        public MinimalRetentionLease(int definitionId = default(int), string ownerId = default(string), int runId = default(int))
        {
            this.DefinitionId = definitionId;
            this.OwnerId = ownerId;
            this.RunId = runId;
        }

        /// <summary>
        /// The pipeline definition of the run.
        /// </summary>
        /// <value>The pipeline definition of the run.</value>
        [DataMember(Name = "definitionId", EmitDefaultValue = false)]
        public int DefinitionId { get; set; }

        /// <summary>
        /// User-provided string that identifies the owner of a retention lease.
        /// </summary>
        /// <value>User-provided string that identifies the owner of a retention lease.</value>
        [DataMember(Name = "ownerId", EmitDefaultValue = false)]
        public string OwnerId { get; set; }

        /// <summary>
        /// The pipeline run to protect.
        /// </summary>
        /// <value>The pipeline run to protect.</value>
        [DataMember(Name = "runId", EmitDefaultValue = false)]
        public int RunId { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class MinimalRetentionLease {\n");
            sb.Append("  DefinitionId: ").Append(DefinitionId).Append("\n");
            sb.Append("  OwnerId: ").Append(OwnerId).Append("\n");
            sb.Append("  RunId: ").Append(RunId).Append("\n");
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
            return this.Equals(input as MinimalRetentionLease);
        }

        /// <summary>
        /// Returns true if MinimalRetentionLease instances are equal
        /// </summary>
        /// <param name="input">Instance of MinimalRetentionLease to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(MinimalRetentionLease input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.DefinitionId == input.DefinitionId ||
                    this.DefinitionId.Equals(input.DefinitionId)
                ) && 
                (
                    this.OwnerId == input.OwnerId ||
                    (this.OwnerId != null &&
                    this.OwnerId.Equals(input.OwnerId))
                ) && 
                (
                    this.RunId == input.RunId ||
                    this.RunId.Equals(input.RunId)
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
                hashCode = (hashCode * 59) + this.DefinitionId.GetHashCode();
                if (this.OwnerId != null)
                {
                    hashCode = (hashCode * 59) + this.OwnerId.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.RunId.GetHashCode();
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
