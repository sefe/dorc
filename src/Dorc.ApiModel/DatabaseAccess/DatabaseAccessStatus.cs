using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dorc.ApiModel
{
    public class DatabaseAccessStatus
    {
        [Required]
        public bool Enabled { get; set; }
        [Required]
        public bool ExecutionEnabled { get; set; }
        public bool CanApproveDbOwner { get; set; }
        [Required]
        public string[] Providers { get; set; } = Array.Empty<string>();
        [Required]
        public string[] Limitations { get; set; } = Array.Empty<string>();
    }
}
