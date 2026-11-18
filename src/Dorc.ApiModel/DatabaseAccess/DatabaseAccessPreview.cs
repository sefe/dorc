using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessPreview
    {
        [Required(AllowEmptyStrings = true)]
        public string Token { get; set; } = "";
        [Required]
        public long Revision { get; set; }
        [Required]
        public DatabaseAccessObservation Observed { get; set; } = new DatabaseAccessObservation();
        [Required]
        public List<DatabaseAccessOperation> Operations { get; set; } = new List<DatabaseAccessOperation>();
        [Required]
        public List<string> Errors { get; set; } = new List<string>();
    }
}
