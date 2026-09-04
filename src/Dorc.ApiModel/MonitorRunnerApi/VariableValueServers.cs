using System;

namespace Dorc.ApiModel.MonitorRunnerApi
{
    public class VariableValueServers
    {
        public string Name { get; set; }
        public string OsName { get; set; }
        public string Tags { get; set; }

        /// <summary>
        /// Superseded by <see cref="Tags"/>; scheduled for removal in the release
        /// after the one that introduces it.
        ///
        /// Kept because PowerShell does not throw on a missing property — a deploy
        /// script reading $server.ApplicationServerName would silently receive
        /// $null and carry on resolving nothing, which is worse than failing. It
        /// returns the same value so both spellings work for one release.
        /// </summary>
        [Obsolete("Renamed to Tags. Removed in the release after this one.")]
        public string ApplicationServerName
        {
            get => Tags;
            set => Tags = value;
        }

        public VariableValueDaemons[] Services { get; set; }
    }
}
