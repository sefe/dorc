namespace Dorc.ApiModel
{
    /// <summary>
    /// Request body for POST /Terraform/templates/{name}/{version}/instantiate.
    /// Creates a Catalog-mode component in the destination project so the
    /// engineer can deploy the stock template through the existing DOrc
    /// deploy flow.
    /// </summary>
    public class TerraformTemplateInstantiateRequestApiModel
    {
        /// <summary>Destination project ID (from the existing project picker).</summary>
        public int ProjectId { get; set; }

        /// <summary>Name to assign to the new component within the project. Defaults to the template name when empty.</summary>
        public string ComponentName { get; set; }

        /// <summary>Optional parent component ID; null for top-level placement.</summary>
        public int? ParentComponentId { get; set; }
    }
}
