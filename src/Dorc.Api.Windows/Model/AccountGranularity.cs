namespace Dorc.Api.Windows.Model
{
    /// <summary>
    /// Specifies the granularity for account queries
    /// </summary>
    public enum AccountGranularity
    {
        NotSet = -1,
        UsersAndGroups = 0,
        Users = 1,
        Groups = 2
    }
}
