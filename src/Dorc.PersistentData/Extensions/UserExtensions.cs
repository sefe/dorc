using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dorc.PersistentData.Extensions
{
    [SupportedOSPlatform("windows")]
    public static class UserExtensions
    {
        public static List<string> GetSidsForUser(this IPrincipal user)
        {
            return (user.Identity?.Name).GetSidsForUser();
        }

        public static List<string> GetSidsForUser(this string username)
        {
            var result = new Dictionary<string, string>();
            var name = username;

            DirectorySearcher ds = new DirectorySearcher();
            if (username.Contains('\\'))
                name = username.Split('\\')[1];

            ds.Filter = $"(&(objectClass=user)(sAMAccountName={name}))";
            SearchResult sr = ds.FindOne();

            DirectoryEntry user = sr.GetDirectoryEntry();
            user.RefreshCache(["tokenGroups"]);

            for (int i = 0; i < user.Properties["tokenGroups"].Count; i++)
            {
                SecurityIdentifier sid = new SecurityIdentifier((byte[])user.Properties["tokenGroups"][i], 0);
                NTAccount nt = (NTAccount)sid.Translate(typeof(NTAccount));
                result.Add(nt.Value, sid.ToString());
            }

            var f = new NTAccount(username);
            var s = (SecurityIdentifier)f.Translate(typeof(SecurityIdentifier));
            var sidString = s.ToString();

            result.Add(username, sidString);

            return result.Values.ToList();
        }

        public static string GetUsername(this IPrincipal user)
        {
            var userSplit = user.Identity.Name.Split('\\');
            var userName = userSplit[1];
            return userName;
        }
    }
}
