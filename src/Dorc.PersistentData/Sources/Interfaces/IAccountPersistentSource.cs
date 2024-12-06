using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IAccountPersistentSource
    {
        Task<bool> UserExistsAsync(string lanId, string accountType);

        Task<bool> GroupExistsAsync(string lanId, string accountType);
    }
}
