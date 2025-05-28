﻿namespace Dorc.Api.Interfaces
{
    public interface IUserGroupsReaderFactory
    {
        IUserGroupReader GetWinAuthUserGroupsReader();
        IUserGroupReader GetOAuthUserGroupsReader();
    }
}