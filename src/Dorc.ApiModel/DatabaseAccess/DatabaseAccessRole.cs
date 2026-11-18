using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessRole
    {
        [Required(AllowEmptyStrings = true)]
        public string Name { get; set; } = "";
        [Required]
        public bool Managed { get; set; }
        [Required]
        public bool Present { get; set; } = true;
    }
}
