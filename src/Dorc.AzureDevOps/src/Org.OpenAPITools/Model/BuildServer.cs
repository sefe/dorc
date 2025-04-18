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
    /// BuildServer
    /// </summary>
    [DataContract(Name = "BuildServer")]
    public partial class BuildServer : IEquatable<BuildServer>, IValidatableObject
    {
        /// <summary>
        /// Defines Status
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum StatusEnum
        {
            /// <summary>
            /// Enum Online for value: online
            /// </summary>
            [EnumMember(Value = "online")]
            Online = 1,

            /// <summary>
            /// Enum Offline for value: offline
            /// </summary>
            [EnumMember(Value = "offline")]
            Offline = 2

        }


        /// <summary>
        /// Gets or Sets Status
        /// </summary>
        [DataMember(Name = "status", EmitDefaultValue = false)]
        public StatusEnum? Status { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="BuildServer" /> class.
        /// </summary>
        /// <param name="agents">agents.</param>
        /// <param name="controller">controller.</param>
        /// <param name="id">id.</param>
        /// <param name="isVirtual">isVirtual.</param>
        /// <param name="messageQueueUrl">messageQueueUrl.</param>
        /// <param name="name">name.</param>
        /// <param name="requireClientCertificates">requireClientCertificates.</param>
        /// <param name="status">status.</param>
        /// <param name="statusChangedDate">statusChangedDate.</param>
        /// <param name="uri">uri.</param>
        /// <param name="url">url.</param>
        /// <param name="version">version.</param>
        public BuildServer(List<BuildAgentReference> agents = default(List<BuildAgentReference>), XamlBuildControllerReference controller = default(XamlBuildControllerReference), int id = default(int), bool isVirtual = default(bool), string messageQueueUrl = default(string), string name = default(string), bool requireClientCertificates = default(bool), StatusEnum? status = default(StatusEnum?), DateTime statusChangedDate = default(DateTime), string uri = default(string), string url = default(string), int version = default(int))
        {
            this.Agents = agents;
            this.Controller = controller;
            this.Id = id;
            this.IsVirtual = isVirtual;
            this.MessageQueueUrl = messageQueueUrl;
            this.Name = name;
            this.RequireClientCertificates = requireClientCertificates;
            this.Status = status;
            this.StatusChangedDate = statusChangedDate;
            this.Uri = uri;
            this.Url = url;
            this._Version = version;
        }

        /// <summary>
        /// Gets or Sets Agents
        /// </summary>
        [DataMember(Name = "agents", EmitDefaultValue = false)]
        public List<BuildAgentReference> Agents { get; set; }

        /// <summary>
        /// Gets or Sets Controller
        /// </summary>
        [DataMember(Name = "controller", EmitDefaultValue = false)]
        public XamlBuildControllerReference Controller { get; set; }

        /// <summary>
        /// Gets or Sets Id
        /// </summary>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public int Id { get; set; }

        /// <summary>
        /// Gets or Sets IsVirtual
        /// </summary>
        [DataMember(Name = "isVirtual", EmitDefaultValue = true)]
        public bool IsVirtual { get; set; }

        /// <summary>
        /// Gets or Sets MessageQueueUrl
        /// </summary>
        [DataMember(Name = "messageQueueUrl", EmitDefaultValue = false)]
        public string MessageQueueUrl { get; set; }

        /// <summary>
        /// Gets or Sets Name
        /// </summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or Sets RequireClientCertificates
        /// </summary>
        [DataMember(Name = "requireClientCertificates", EmitDefaultValue = true)]
        public bool RequireClientCertificates { get; set; }

        /// <summary>
        /// Gets or Sets StatusChangedDate
        /// </summary>
        [DataMember(Name = "statusChangedDate", EmitDefaultValue = false)]
        public DateTime StatusChangedDate { get; set; }

        /// <summary>
        /// Gets or Sets Uri
        /// </summary>
        [DataMember(Name = "uri", EmitDefaultValue = false)]
        public string Uri { get; set; }

        /// <summary>
        /// Gets or Sets Url
        /// </summary>
        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        /// <summary>
        /// Gets or Sets _Version
        /// </summary>
        [DataMember(Name = "version", EmitDefaultValue = false)]
        public int _Version { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class BuildServer {\n");
            sb.Append("  Agents: ").Append(Agents).Append("\n");
            sb.Append("  Controller: ").Append(Controller).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  IsVirtual: ").Append(IsVirtual).Append("\n");
            sb.Append("  MessageQueueUrl: ").Append(MessageQueueUrl).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  RequireClientCertificates: ").Append(RequireClientCertificates).Append("\n");
            sb.Append("  Status: ").Append(Status).Append("\n");
            sb.Append("  StatusChangedDate: ").Append(StatusChangedDate).Append("\n");
            sb.Append("  Uri: ").Append(Uri).Append("\n");
            sb.Append("  Url: ").Append(Url).Append("\n");
            sb.Append("  _Version: ").Append(_Version).Append("\n");
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
            return this.Equals(input as BuildServer);
        }

        /// <summary>
        /// Returns true if BuildServer instances are equal
        /// </summary>
        /// <param name="input">Instance of BuildServer to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(BuildServer input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.Agents == input.Agents ||
                    this.Agents != null &&
                    input.Agents != null &&
                    this.Agents.SequenceEqual(input.Agents)
                ) && 
                (
                    this.Controller == input.Controller ||
                    (this.Controller != null &&
                    this.Controller.Equals(input.Controller))
                ) && 
                (
                    this.Id == input.Id ||
                    this.Id.Equals(input.Id)
                ) && 
                (
                    this.IsVirtual == input.IsVirtual ||
                    this.IsVirtual.Equals(input.IsVirtual)
                ) && 
                (
                    this.MessageQueueUrl == input.MessageQueueUrl ||
                    (this.MessageQueueUrl != null &&
                    this.MessageQueueUrl.Equals(input.MessageQueueUrl))
                ) && 
                (
                    this.Name == input.Name ||
                    (this.Name != null &&
                    this.Name.Equals(input.Name))
                ) && 
                (
                    this.RequireClientCertificates == input.RequireClientCertificates ||
                    this.RequireClientCertificates.Equals(input.RequireClientCertificates)
                ) && 
                (
                    this.Status == input.Status ||
                    this.Status.Equals(input.Status)
                ) && 
                (
                    this.StatusChangedDate == input.StatusChangedDate ||
                    (this.StatusChangedDate != null &&
                    this.StatusChangedDate.Equals(input.StatusChangedDate))
                ) && 
                (
                    this.Uri == input.Uri ||
                    (this.Uri != null &&
                    this.Uri.Equals(input.Uri))
                ) && 
                (
                    this.Url == input.Url ||
                    (this.Url != null &&
                    this.Url.Equals(input.Url))
                ) && 
                (
                    this._Version == input._Version ||
                    this._Version.Equals(input._Version)
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
                if (this.Agents != null)
                {
                    hashCode = (hashCode * 59) + this.Agents.GetHashCode();
                }
                if (this.Controller != null)
                {
                    hashCode = (hashCode * 59) + this.Controller.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Id.GetHashCode();
                hashCode = (hashCode * 59) + this.IsVirtual.GetHashCode();
                if (this.MessageQueueUrl != null)
                {
                    hashCode = (hashCode * 59) + this.MessageQueueUrl.GetHashCode();
                }
                if (this.Name != null)
                {
                    hashCode = (hashCode * 59) + this.Name.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.RequireClientCertificates.GetHashCode();
                hashCode = (hashCode * 59) + this.Status.GetHashCode();
                if (this.StatusChangedDate != null)
                {
                    hashCode = (hashCode * 59) + this.StatusChangedDate.GetHashCode();
                }
                if (this.Uri != null)
                {
                    hashCode = (hashCode * 59) + this.Uri.GetHashCode();
                }
                if (this.Url != null)
                {
                    hashCode = (hashCode * 59) + this.Url.GetHashCode();
                }
                hashCode = (hashCode * 59) + this._Version.GetHashCode();
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
