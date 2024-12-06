using Dorc.Core.Account.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dorc.Core.Account
{
    public interface IAccountExistenceChecker
    {
        bool UserExists(string lanId, AccountType accountType);

        bool GroupExists(string lanId, AccountType accountType);
    }
}
