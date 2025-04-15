using System.ComponentModel;
using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;

namespace Dorc.Core
{
    [SupportedOSPlatform("windows")]
    public class ActiveDirectorySearcher : IActiveDirectorySearcher
    {
        private readonly DirectoryEntry _activeDirectoryRoot;
        private readonly ILog _log;
        private readonly IUsersPersistentSource _usersPersistentSource;
        private static readonly string[] adProps = { "cn", "displayname", "objectsid", "mail" };

        public ActiveDirectorySearcher(string domainName, ILog log, IUsersPersistentSource usersPersistentSource)
        {
            _usersPersistentSource = usersPersistentSource;
            _log = log;

            var context = new DirectoryContext(DirectoryContextType.Domain, domainName);
            var d = Domain.GetDomain(context);
            var de = d.GetDirectoryEntry();
            var ldapRoot = $"LDAP://{de.Properties["DistinguishedName"].Value}";
            _activeDirectoryRoot = new DirectoryEntry(ldapRoot);
        }

        public List<DirectoryEntry> Search(string objectName)
        {
            var output = new List<DirectoryEntry>();

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
                                output.Add(de);
                        }
                        if (de.Properties["objectClass"]?.Contains("group") == true)
                        {
                            output.Add(de);
                        }
                    }
                }
            }

            return output;
        }

        [DllImport("advapi32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool ConvertSidToStringSid([MarshalAs(UnmanagedType.LPArray)] byte[] pSID,
            out nint ptrSid);

        public static string? GetSidString(byte[] sid)
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

        public UserApiModel GetUserByLanId(string lanId)
        {
            var user = _usersPersistentSource.GetUser(lanId);

            // Can't find in the DB now look in AD
            if (user != null)
                return user;

            try
            {
                var activeDirectoryElementApiModel = GetUserIdActiveDirectory(lanId);
                return new UserApiModel
                {
                    DisplayName = activeDirectoryElementApiModel.DisplayName,
                    LanId = activeDirectoryElementApiModel.Username,
                    LoginId = activeDirectoryElementApiModel.Username,
                    LoginType = "Windows",
                    LanIdType = activeDirectoryElementApiModel.IsGroup ? "GROUP" : "USER"
                };
            }
            catch (Exception)
            {
                var errMsg = $"Unable to locate user {lanId}";
                _log.Warn(errMsg);
            }

            return null;
        }

        public ActiveDirectoryElementApiModel GetUserIdActiveDirectory(string id)
        {
            if (!Regex.IsMatch(id, @"^[a-zA-Z'-_. ]+(\(External\))?$"))
            {
                throw new ArgumentException("Invalid search criteria. Search criteria must be \"^[a-zA-Z-_. ]+(\\(External\\))?$\"!");
            }

            using (var dirSearcher = new DirectorySearcher(new DirectoryEntry())
            {
                SearchScope = SearchScope.Subtree,
                Filter = string.Format("(&(objectClass=user)(|(cn={0})(sn={0}*)(givenName={0})(DisplayName={0}*)(sAMAccountName={0}*)))",
                    id)
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

                        var user = new ActiveDirectoryElementApiModel
                        {
                            Username = de.Properties.Contains("SAMAccountName") ? de.Properties["SAMAccountName"][0].ToString() : string.Empty,
                            DisplayName = de.Properties.Contains("DisplayName") ? de.Properties["DisplayName"][0].ToString() : string.Empty,
                            Sid = Sid,
                            IsGroup = de.Properties["objectClass"]?.Contains("group") == true,
                            Email = de.Properties["mail"].Value != null ? de.Properties["mail"].Value?.ToString() : de.Properties["UserPrincipalName"].Value?.ToString()
                        };

                        return user;
                    }
                }
            }

            throw new ArgumentException("Failed to locate a valid user account for requested user!");
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