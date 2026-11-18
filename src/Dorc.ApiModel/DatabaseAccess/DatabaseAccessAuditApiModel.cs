using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessAuditApiModel
    {
        [Required]
        public long Id { get; set; }
        [Required]
        public DateTime CreatedUtc { get; set; }
        [Required(AllowEmptyStrings = true)]
        public string Actor { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Action { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Status { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Detail { get; set; } = "";
    }
}
