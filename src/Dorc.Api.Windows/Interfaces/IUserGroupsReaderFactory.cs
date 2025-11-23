namespace Dorc.Api.Windows.Interfaces
{
    public interface IUserGroupsReaderFactory
    {
        IUserGroupReader GetWinAuthUserGroupsReader();
        IUserGroupReader GetOAuthUserGroupsReader();
    }
}