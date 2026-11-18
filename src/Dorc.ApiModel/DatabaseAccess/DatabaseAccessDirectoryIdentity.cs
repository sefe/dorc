using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessDirectoryIdentity
    {
        [Required(AllowEmptyStrings = true)]
        public string Id { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string DisplayName { get; set; } = "";
        [Required]
        public bool IsGroup { get; set; }
        public string AccountName { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string Status { get; set; } = "Resolved";
    }
}
