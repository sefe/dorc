namespace Dorc.Terraform.Catalog
{
    public interface ITemplateCatalog
    {
        Task<IReadOnlyList<TerraformTemplateManifest>> ListAsync(CancellationToken cancellationToken = default);

        Task<TerraformTemplateManifest?> GetAsync(string name, CancellationToken cancellationToken = default);

        Task<TerraformTemplateManifest?> GetAsync(string name, string version, CancellationToken cancellationToken = default);
    }
}
