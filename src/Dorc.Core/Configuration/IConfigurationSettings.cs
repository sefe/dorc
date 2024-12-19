namespace Dorc.Core.Configuration
{
    public interface IConfigurationSettings
    {
        string GetConfigurationDomainName();

        string GetConfigurationDomainNameIntra();
    }
}
