using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessConfirmation
    {
        [Required(AllowEmptyStrings = true)]
        public string Token { get; set; } = "";
    }
}
