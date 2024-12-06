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
    /// Represents a Subversion mapping entry.
    /// </summary>
    [DataContract(Name = "SvnMappingDetails")]
    public partial class SvnMappingDetails : IEquatable<SvnMappingDetails>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SvnMappingDetails" /> class.
        /// </summary>
        /// <param name="depth">The depth..</param>
        /// <param name="ignoreExternals">Indicates whether to ignore externals..</param>
        /// <param name="localPath">The local path..</param>
        /// <param name="revision">The revision..</param>
        /// <param name="serverPath">The server path..</param>
        public SvnMappingDetails(int depth = default(int), bool ignoreExternals = default(bool), string localPath = default(string), string revision = default(string), string serverPath = default(string))
        {
            this.Depth = depth;
            this.IgnoreExternals = ignoreExternals;
            this.LocalPath = localPath;
            this.Revision = revision;
            this.ServerPath = serverPath;
        }

        /// <summary>
        /// The depth.
        /// </summary>
        /// <value>The depth.</value>
        [DataMember(Name = "depth", EmitDefaultValue = false)]
        public int Depth { get; set; }

        /// <summary>
        /// Indicates whether to ignore externals.
        /// </summary>
        /// <value>Indicates whether to ignore externals.</value>
        [DataMember(Name = "ignoreExternals", EmitDefaultValue = true)]
        public bool IgnoreExternals { get; set; }

        /// <summary>
        /// The local path.
        /// </summary>
        /// <value>The local path.</value>
        [DataMember(Name = "localPath", EmitDefaultValue = false)]
        public string LocalPath { get; set; }

        /// <summary>
        /// The revision.
        /// </summary>
        /// <value>The revision.</value>
        [DataMember(Name = "revision", EmitDefaultValue = false)]
        public string Revision { get; set; }

        /// <summary>
        /// The server path.
        /// </summary>
        /// <value>The server path.</value>
        [DataMember(Name = "serverPath", EmitDefaultValue = false)]
        public string ServerPath { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class SvnMappingDetails {\n");
            sb.Append("  Depth: ").Append(Depth).Append("\n");
            sb.Append("  IgnoreExternals: ").Append(IgnoreExternals).Append("\n");
            sb.Append("  LocalPath: ").Append(LocalPath).Append("\n");
            sb.Append("  Revision: ").Append(Revision).Append("\n");
            sb.Append("  ServerPath: ").Append(ServerPath).Append("\n");
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
            return this.Equals(input as SvnMappingDetails);
        }

        /// <summary>
        /// Returns true if SvnMappingDetails instances are equal
        /// </summary>
        /// <param name="input">Instance of SvnMappingDetails to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(SvnMappingDetails input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Depth == input.Depth ||
                    this.Depth.Equals(input.Depth)
                ) && 
                (
                    this.IgnoreExternals == input.IgnoreExternals ||
                    this.IgnoreExternals.Equals(input.IgnoreExternals)
                ) && 
                (
                    this.LocalPath == input.LocalPath ||
                    (this.LocalPath != null &&
                    this.LocalPath.Equals(input.LocalPath))
                ) && 
                (
                    this.Revision == input.Revision ||
                    (this.Revision != null &&
                    this.Revision.Equals(input.Revision))
                ) && 
                (
                    this.ServerPath == input.ServerPath ||
                    (this.ServerPath != null &&
                    this.ServerPath.Equals(input.ServerPath))
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
                hashCode = (hashCode * 59) + this.Depth.GetHashCode();
                hashCode = (hashCode * 59) + this.IgnoreExternals.GetHashCode();
                if (this.LocalPath != null)
                {
                    hashCode = (hashCode * 59) + this.LocalPath.GetHashCode();
                }
                if (this.Revision != null)
                {
                    hashCode = (hashCode * 59) + this.Revision.GetHashCode();
                }
                if (this.ServerPath != null)
                {
                    hashCode = (hashCode * 59) + this.ServerPath.GetHashCode();
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
