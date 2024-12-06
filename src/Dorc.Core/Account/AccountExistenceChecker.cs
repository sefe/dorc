using Dorc.Core.Account.Models;
using Dorc.PersistentData.Sources.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dorc.Core.Account
{
    public class AccountExistenceChecker : IAccountExistenceChecker
    {
        private readonly IAccountPersistentSource accountPersistentSource;

        public AccountExistenceChecker(IAccountPersistentSource accountPersistentSource)
        {
            this.accountPersistentSource = accountPersistentSource;
        }

        public bool UserExists(string lanId, AccountType accountType)
        {
            return accountPersistentSource.UserExistsAsync(lanId, accountType.ToString()).Result;
        }

        public bool GroupExists(string lanId, AccountType accountType)
        {
            return accountPersistentSource.GroupExistsAsync(lanId, accountType.ToString()).Result;
        }
    }
}
