using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessOperation
    {
        [Required(AllowEmptyStrings = true)]
        public string Action { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Principal { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Role { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Login { get; set; } = "";
    }
}
