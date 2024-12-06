using System;
using System.Collections.Generic;
using System.Text;

namespace Dorc.ApiModel
{
    public class ConfigValueApiModel
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public bool Secure { get; set; }
    }
}
