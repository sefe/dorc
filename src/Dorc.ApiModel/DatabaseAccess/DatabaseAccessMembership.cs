using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessMembership
    {
        [Required(AllowEmptyStrings = true)]
        public string Principal { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Role { get; set; } = "";
        [Required]
        public bool Present { get; set; } = true;
    }
}
