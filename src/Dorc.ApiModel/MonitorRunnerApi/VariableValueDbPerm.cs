using System;

namespace Dorc.ApiModel.MonitorRunnerApi
{
    public class DatabaseDefinition
    {
        public string Name { get; set; }
        public string Tags { get; set; }

        /// <summary>
        /// Superseded by <see cref="Tags"/>; scheduled for removal in the release
        /// after the one that introduces it.
        ///
        /// Kept because PowerShell does not throw on a missing property — a deploy
        /// script reading $db.Database.Type would silently receive $null and carry
        /// on resolving nothing, which is worse than failing. It returns the same
        /// value so both spellings work for one release.
        /// </summary>
        [Obsolete("Renamed to Tags. Removed in the release after this one.")]
        public string Type
        {
            get => Tags;
            set => Tags = value;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;

                hash += 23 * Name.GetHashCode();
                hash += 23 * Tags.GetHashCode();

                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as DatabaseDefinition;
            if (other == null) return false;

            return Name == other.Name && Tags == other.Tags;
        }
    }
    public class DbUserRole
    {
        public string User { get; }
        public string[] Roles { get; }

        public DbUserRole(string user, string[] roles)
        {
            User = user;
            Roles = roles;
        }
    }

    public class VariableValueDbPerm
    {
        public DatabaseDefinition Database { get; set; }
        public DbUserRole[] Users { get; set; }
    }
}
