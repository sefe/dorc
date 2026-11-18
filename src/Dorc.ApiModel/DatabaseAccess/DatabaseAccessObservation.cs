using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessObservation
    {
        [Required]
        public List<DatabaseAccessObservedPrincipal> Principals { get; set; } = new List<DatabaseAccessObservedPrincipal>();
        [Required]
        public List<DatabaseAccessRole> Roles { get; set; } = new List<DatabaseAccessRole>();
        [Required]
        public List<DatabaseAccessMembership> Memberships { get; set; } = new List<DatabaseAccessMembership>();
        [Required]
        public List<DatabaseAccessObservedPrincipal> Logins { get; set; } = new List<DatabaseAccessObservedPrincipal>();
    }
}
