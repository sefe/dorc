using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessPrincipal
    {
        [Required(AllowEmptyStrings = true)]
        public string Name { get; set; } = "";
        // Native, DirectoryUser or DirectoryGroup. Name is a database alias, not an AD profile.
        [Required(AllowEmptyStrings = true)]
        public string Kind { get; set; } = "Native";
        public string LoginName { get; set; }
        public string DirectoryId { get; set; }
        [Required]
        public bool Present { get; set; } = true;
        public int? LegacyUserId { get; set; }
    }
}
