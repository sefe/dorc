using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using log4net;
using System.ComponentModel;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Dorc.Core
{
    [SupportedOSPlatform("windows")]
    public class ActiveDirectorySearcher : IActiveDirectorySearcher
    {
        private readonly DirectoryEntry _activeDirectoryRoot;
        private readonly ILog _log;
        private static readonly string[] adProps = { "cn", "displayname", "objectsid", "mail" };

        public ActiveDirectorySearcher(string domainName, ILog log)
        {
            _log = log;

            var context = new DirectoryContext(DirectoryContextType.Domain, domainName);
            var d = Domain.GetDomain(context);
            var de = d.GetDirectoryEntry();
            var ldapRoot = $"LDAP://{de.Properties["DistinguishedName"].Value}";
            _activeDirectoryRoot = new DirectoryEntry(ldapRoot);
        }

        public List<UserElementApiModel> Search(string objectName)
        {
            var output = new List<UserElementApiModel>();

            // restrict the username and password to letters only
            if (!Regex.IsMatch(objectName, "^[a-zA-Z-_. ]+$"))
            {
                return output;
            }

            using (var searcher = new DirectorySearcher(_activeDirectoryRoot)
                {
                    Filter = $"(&(anr={objectName})(|(objectCategory=group)(objectCategory=person)))"
                })
            {
                searcher.PropertiesToLoad.AddRange(adProps);

                using (var searchResults = searcher.FindAll())
                {
                    foreach (SearchResult sr in searchResults)
                    {
                        var de = sr.GetDirectoryEntry();

                        if (de.NativeGuid == null)
                        {
                            throw new ArgumentException("Failed to find a valid user giud for the AD Account of requester!");
                        }

                        if (de.Properties["objectClass"]?.Contains("user") == true)
                        {
                            var flags = (int)de.Properties["userAccountControl"].Value;

                            var enabled = !Convert.ToBoolean(flags & 0x0002);
                            if (enabled)
                                output.Add(GetModelFromDirectoryEntry(de));
                        }
                        if (de.Properties["objectClass"]?.Contains("group") == true)
                        {
                            output.Add(GetModelFromDirectoryEntry(de));
                        }
                    }
                }
            }

            return output;
        }

        private UserElementApiModel GetModelFromDirectoryEntry(DirectoryEntry de)
        {
            var displayName = GetSafeString(de.Properties, "displayName");
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = GetSafeString(de.Properties, "cn");
            }

            return new UserElementApiModel()
            {
                Username = GetSafeString(de.Properties, "SAMAccountName"),
                DisplayName = displayName,
                Pid = GetSidString((byte[])de.Properties["objectSid"].Value),
                IsGroup = de.Properties["objectClass"]?.Contains("group") == true,
                Email = de.Properties["mail"].Value != null ? de.Properties["mail"].Value?.ToString() : de.Properties["UserPrincipalName"].Value?.ToString()
            };
        }

        [DllImport("advapi32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool ConvertSidToStringSid([MarshalAs(UnmanagedType.LPArray)] byte[] pSID,
            out nint ptrSid);

        private static string? GetSidString(byte[] sid)
        {
            string? sidString;
            if (!ConvertSidToStringSid(sid, out var ptrSid))
                throw new Win32Exception();
            try
            {
                sidString = Marshal.PtrToStringAuto(ptrSid);
            }
            finally
            {
                Marshal.FreeHGlobal(ptrSid);
            }

            return sidString;
        }

        private byte[] ConvertSidStringToByteArray(string sid)
        {
            var securityIdentifier = new SecurityIdentifier(sid);

            byte[] sidBytes = new byte[securityIdentifier.BinaryLength];
            securityIdentifier.GetBinaryForm(sidBytes, 0);

            return sidBytes;
        }

        public UserElementApiModel GetUserDataById(string sid)
        {
            if (string.IsNullOrEmpty(sid))
            {
                throw new ArgumentException("SID cannot be null or empty.");
            }

            byte[] sidBytes = ConvertSidStringToByteArray(sid);

            using (var dirSearcher = new DirectorySearcher(new DirectoryEntry())
            {
                SearchScope = SearchScope.Subtree,
                Filter = $"(objectSid={Encoding.ASCII.GetString(sidBytes)})"
            })
            {
                dirSearcher.PropertiesToLoad.Add("mail");        // smtp mail address
                dirSearcher.PropertiesToLoad.Add("displayName");
                dirSearcher.PropertiesToLoad.Add("sAMAccountName");

                using (var searchResults = dirSearcher.FindAll())
                {
                    foreach (SearchResult sr in searchResults)
                    {
                        var de = sr.GetDirectoryEntry();

                        if (!IsActive(de))
                        {
                            continue;
                        }

                        var entity = GetModelFromDirectoryEntry(de);

                        entity.Pid = sid;

                        return entity;
                    }
                }
            }

            throw new ArgumentException($"Failed to locate an entity with SID: {sid}");
        }

        public UserElementApiModel GetUserData(string name)
        {
            if (!Regex.IsMatch(name, @"^[a-zA-Z'-_. ]+(\(External\))?$"))
            {
                throw new ArgumentException("Invalid search criteria. Search criteria must be \"^[a-zA-Z-_. ]+(\\(External\\))?$\"!");
            }

            using (var dirSearcher = new DirectorySearcher(new DirectoryEntry())
            {
                SearchScope = SearchScope.Subtree,
                Filter = string.Format("(&(objectClass=user)(|(cn={0})(sn={0}*)(givenName={0})(DisplayName={0}*)(sAMAccountName={0}*)))",
                    name)
            })
            {
                dirSearcher.PropertiesToLoad.Add("mail");        // smtp mail address
                using (var searchResults = dirSearcher.FindAll())
                {
                    foreach (SearchResult sr in searchResults)
                    {
                        var Sid = string.Empty;
                        var de = sr.GetDirectoryEntry();
                        var obVal = de.Properties["objectSid"].Value;
                        if (null != obVal)
                        {
                            Sid = ConvertByteToStringSid((byte[])obVal);
                        }

                        if (!IsActive(de))
                        { continue; }

                        var user = GetModelFromDirectoryEntry(de);
                        user.Pid = Sid;

                        return user;
                    }
                }
            }

            throw new ArgumentException("Failed to locate a valid user account for requested user!");
        }

        public List<string> GetSidsForUser(string username)
        {
            var result = new HashSet<string>();
            var name = username;

            DirectorySearcher ds = new DirectorySearcher();

            ds.Filter = $"(&(objectClass=user)(sAMAccountName={name}))";
            SearchResult sr = ds.FindOne();

            DirectoryEntry user = sr.GetDirectoryEntry();
            user.RefreshCache(new string[] { "tokenGroups" });

            for (int i = 0; i < user.Properties["tokenGroups"].Count; i++)
            {
                SecurityIdentifier sid = new SecurityIdentifier((byte[])user.Properties["tokenGroups"][i], 0);
                result.Add(sid.ToString());
            }

            var f = new NTAccount(username);
            var s = (SecurityIdentifier)f.Translate(typeof(SecurityIdentifier));
            var sidString = s.ToString();

            result.Add(sidString);

            var sidList = result.ToList();

            return sidList;
        }

        public string? GetGroupSidIfUserIsMemberRecursive(string userName, string groupName, string domainName)
        {
            using (var context = new PrincipalContext(ContextType.Domain, null, domainName))
            {
                try
                {
                    using (var groupPrincipal = GroupPrincipal.FindByIdentity(context, IdentityType.SamAccountName, groupName))
                    {
                        if (groupPrincipal != null)
                        {
                            var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
                            if (userPrincipal != null && groupPrincipal.GetMembers(true).Contains(userPrincipal))
                            {
                                return groupPrincipal.Sid.Value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new System.Configuration.Provider.ProviderException("Unable to query Active Directory.", ex);
                }
            }

            return string.Empty;
        }

        private static bool IsActive(DirectoryEntry de)
        {
            if (de.NativeGuid == null) return false;

            var flags = (int)de.Properties["userAccountControl"].Value;

            return !Convert.ToBoolean(flags & 0x0002);
        }

        private static string ConvertByteToStringSid(byte[] sidBytes)
        {
            var strSid = new StringBuilder();
            strSid.Append("S-");
            try
            {
                // Add SID revision.
                strSid.Append(sidBytes[0].ToString());
                // Next six bytes are SID authority value.
                if (sidBytes[6] != 0 || sidBytes[5] != 0)
                {
                    var strAuth =
                        $"0x{(short)sidBytes[1]:2x}{(short)sidBytes[2]:2x}{(short)sidBytes[3]:2x}{(short)sidBytes[4]:2x}{(short)sidBytes[5]:2x}{(short)sidBytes[6]:2x}";
                    strSid.Append("-");
                    strSid.Append(strAuth);
                }
                else
                {
                    long iVal = sidBytes[1] +
                                 (sidBytes[2] << 8) +
                                 (sidBytes[3] << 16) +
                                 (sidBytes[4] << 24);
                    strSid.Append("-");
                    strSid.Append(iVal.ToString());
                }

                // Get sub authority count...
                var iSubCount = Convert.ToInt32(sidBytes[7]);
                for (var i = 0; i < iSubCount; i++)
                {
                    var idxAuth = 8 + i * 4;
                    var iSubAuth = BitConverter.ToUInt32(sidBytes, idxAuth);
                    strSid.Append("-");
                    strSid.Append(iSubAuth.ToString());
                }
            }
            catch (Exception)
            {
                return "";
            }
            return strSid.ToString();
        }
        private string GetSafeString(PropertyCollection properties, string propertyName)
        {
            return properties[propertyName]?.OfType<string>().FirstOrDefault() ?? string.Empty;
        }
    }
}