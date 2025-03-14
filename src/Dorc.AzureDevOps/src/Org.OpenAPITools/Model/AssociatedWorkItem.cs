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
    /// AssociatedWorkItem
    /// </summary>
    [DataContract(Name = "AssociatedWorkItem")]
    public partial class AssociatedWorkItem : IEquatable<AssociatedWorkItem>, IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AssociatedWorkItem" /> class.
        /// </summary>
        /// <param name="assignedTo">assignedTo.</param>
        /// <param name="id">Id of associated the work item..</param>
        /// <param name="state">state.</param>
        /// <param name="title">title.</param>
        /// <param name="url">REST Url of the work item..</param>
        /// <param name="webUrl">webUrl.</param>
        /// <param name="workItemType">workItemType.</param>
        public AssociatedWorkItem(string assignedTo = default(string), int id = default(int), string state = default(string), string title = default(string), string url = default(string), string webUrl = default(string), string workItemType = default(string))
        {
            this.AssignedTo = assignedTo;
            this.Id = id;
            this.State = state;
            this.Title = title;
            this.Url = url;
            this.WebUrl = webUrl;
            this.WorkItemType = workItemType;
        }

        /// <summary>
        /// Gets or Sets AssignedTo
        /// </summary>
        [DataMember(Name = "assignedTo", EmitDefaultValue = false)]
        public string AssignedTo { get; set; }

        /// <summary>
        /// Id of associated the work item.
        /// </summary>
        /// <value>Id of associated the work item.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public int Id { get; set; }

        /// <summary>
        /// Gets or Sets State
        /// </summary>
        [DataMember(Name = "state", EmitDefaultValue = false)]
        public string State { get; set; }

        /// <summary>
        /// Gets or Sets Title
        /// </summary>
        [DataMember(Name = "title", EmitDefaultValue = false)]
        public string Title { get; set; }

        /// <summary>
        /// REST Url of the work item.
        /// </summary>
        /// <value>REST Url of the work item.</value>
        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        /// <summary>
        /// Gets or Sets WebUrl
        /// </summary>
        [DataMember(Name = "webUrl", EmitDefaultValue = false)]
        public string WebUrl { get; set; }

        /// <summary>
        /// Gets or Sets WorkItemType
        /// </summary>
        [DataMember(Name = "workItemType", EmitDefaultValue = false)]
        public string WorkItemType { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class AssociatedWorkItem {\n");
            sb.Append("  AssignedTo: ").Append(AssignedTo).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  State: ").Append(State).Append("\n");
            sb.Append("  Title: ").Append(Title).Append("\n");
            sb.Append("  Url: ").Append(Url).Append("\n");
            sb.Append("  WebUrl: ").Append(WebUrl).Append("\n");
            sb.Append("  WorkItemType: ").Append(WorkItemType).Append("\n");
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
            return this.Equals(input as AssociatedWorkItem);
        }

        /// <summary>
        /// Returns true if AssociatedWorkItem instances are equal
        /// </summary>
        /// <param name="input">Instance of AssociatedWorkItem to be compared</param>
        /// <returns>Boolean</returns>
        public bool Equals(AssociatedWorkItem input)
        {
            if (input == null)
            {
                return false;
            }
            return 
                (
                    this.AssignedTo == input.AssignedTo ||
                    (this.AssignedTo != null &&
                    this.AssignedTo.Equals(input.AssignedTo))
                ) && 
                (
                    this.Id == input.Id ||
                    this.Id.Equals(input.Id)
                ) && 
                (
                    this.State == input.State ||
                    (this.State != null &&
                    this.State.Equals(input.State))
                ) && 
                (
                    this.Title == input.Title ||
                    (this.Title != null &&
                    this.Title.Equals(input.Title))
                ) && 
                (
                    this.Url == input.Url ||
                    (this.Url != null &&
                    this.Url.Equals(input.Url))
                ) && 
                (
                    this.WebUrl == input.WebUrl ||
                    (this.WebUrl != null &&
                    this.WebUrl.Equals(input.WebUrl))
                ) && 
                (
                    this.WorkItemType == input.WorkItemType ||
                    (this.WorkItemType != null &&
                    this.WorkItemType.Equals(input.WorkItemType))
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
                if (this.AssignedTo != null)
                {
                    hashCode = (hashCode * 59) + this.AssignedTo.GetHashCode();
                }
                hashCode = (hashCode * 59) + this.Id.GetHashCode();
                if (this.State != null)
                {
                    hashCode = (hashCode * 59) + this.State.GetHashCode();
                }
                if (this.Title != null)
                {
                    hashCode = (hashCode * 59) + this.Title.GetHashCode();
                }
                if (this.Url != null)
                {
                    hashCode = (hashCode * 59) + this.Url.GetHashCode();
                }
                if (this.WebUrl != null)
                {
                    hashCode = (hashCode * 59) + this.WebUrl.GetHashCode();
                }
                if (this.WorkItemType != null)
                {
                    hashCode = (hashCode * 59) + this.WorkItemType.GetHashCode();
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
