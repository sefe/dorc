using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessState
    {
        [Required]
        public int DatabaseId { get; set; }
        [Required]
        public long Revision { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string Provider { get; set; } = "sql-server";
        [Required]
        public List<DatabaseAccessPrincipal> Principals { get; set; } = new List<DatabaseAccessPrincipal>();
        [Required]
        public List<DatabaseAccessRole> Roles { get; set; } = new List<DatabaseAccessRole>();
        [Required]
        public List<DatabaseAccessMembership> Memberships { get; set; } = new List<DatabaseAccessMembership>();
    }
}
