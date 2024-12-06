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
    /// TaskSourceDefinitionBase
    /// </summary>
    [DataContract(Name = "TaskSourceDefinitionBase")]
    public partial class TaskSourceDefinitionBase : IEquatable<TaskSourceDefinitionBase>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TaskSourceDefinitionBase" /> class.
        /// </summary>
        /// <param name="authKey">authKey.</param>
        /// <param name="endpoint">endpoint.</param>
        /// <param name="keySelector">keySelector.</param>
        /// <param name="selector">selector.</param>
        /// <param name="target">target.</param>
        public TaskSourceDefinitionBase(string authKey = default(string), string endpoint = default(string), string keySelector = default(string), string selector = default(string), string target = default(string))
        {
            this.AuthKey = authKey;
            this.Endpoint = endpoint;
            this.KeySelector = keySelector;
            this.Selector = selector;
            this.Target = target;
        }

        /// <summary>
        /// Gets or Sets AuthKey
        /// </summary>
        [DataMember(Name = "authKey", EmitDefaultValue = false)]
        public string AuthKey { get; set; }

        /// <summary>
        /// Gets or Sets Endpoint
        /// </summary>
        [DataMember(Name = "endpoint", EmitDefaultValue = false)]
        public string Endpoint { get; set; }

        /// <summary>
        /// Gets or Sets KeySelector
        /// </summary>
        [DataMember(Name = "keySelector", EmitDefaultValue = false)]
        public string KeySelector { get; set; }

        /// <summary>
        /// Gets or Sets Selector
        /// </summary>
        [DataMember(Name = "selector", EmitDefaultValue = false)]
        public string Selector { get; set; }

        /// <summary>
        /// Gets or Sets Target
        /// </summary>
        [DataMember(Name = "target", EmitDefaultValue = false)]
        public string Target { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class TaskSourceDefinitionBase {\n");
            sb.Append("  AuthKey: ").Append(AuthKey).Append("\n");
            sb.Append("  Endpoint: ").Append(Endpoint).Append("\n");
            sb.Append("  KeySelector: ").Append(KeySelector).Append("\n");
            sb.Append("  Selector: ").Append(Selector).Append("\n");
            sb.Append("  Target: ").Append(Target).Append("\n");
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
            return this.Equals(input as TaskSourceDefinitionBase);
        }

        /// <summary>
        /// Returns true if TaskSourceDefinitionBase instances are equal
        /// </summary>
        /// <param name="input">Instance of TaskSourceDefinitionBase to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(TaskSourceDefinitionBase input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.AuthKey == input.AuthKey ||
                    (this.AuthKey != null &&
                    this.AuthKey.Equals(input.AuthKey))
                ) && 
                (
                    this.Endpoint == input.Endpoint ||
                    (this.Endpoint != null &&
                    this.Endpoint.Equals(input.Endpoint))
                ) && 
                (
                    this.KeySelector == input.KeySelector ||
                    (this.KeySelector != null &&
                    this.KeySelector.Equals(input.KeySelector))
                ) && 
                (
                    this.Selector == input.Selector ||
                    (this.Selector != null &&
                    this.Selector.Equals(input.Selector))
                ) && 
                (
                    this.Target == input.Target ||
                    (this.Target != null &&
                    this.Target.Equals(input.Target))
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
                if (this.AuthKey != null)
                {
                    hashCode = (hashCode * 59) + this.AuthKey.GetHashCode();
                }
                if (this.Endpoint != null)
                {
                    hashCode = (hashCode * 59) + this.Endpoint.GetHashCode();
                }
                if (this.KeySelector != null)
                {
                    hashCode = (hashCode * 59) + this.KeySelector.GetHashCode();
                }
                if (this.Selector != null)
                {
                    hashCode = (hashCode * 59) + this.Selector.GetHashCode();
                }
                if (this.Target != null)
                {
                    hashCode = (hashCode * 59) + this.Target.GetHashCode();
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
