using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessObservedPrincipal
    {
        [Required(AllowEmptyStrings = true)]
        public string Name { get; set; } = "";
        [Required(AllowEmptyStrings = true)]
        public string Kind { get; set; } = "";
        public string LoginName { get; set; }
        public string DirectoryId { get; set; }
        [Required]
        public bool Protected { get; set; }
    }
}
