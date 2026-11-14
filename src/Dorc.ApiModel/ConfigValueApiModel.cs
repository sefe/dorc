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
        public Nullable<bool> IsForProd { get; set; }

        /// <summary>
        /// Whether this value may be resolved into deployment script scope. Null on an update
        /// means "not stated", so a client that does not know about the classification cannot
        /// change it by leaving the field out; null on a create takes the default for the
        /// value's kind (hidden when secure, visible otherwise).
        /// </summary>
        public Nullable<bool> VisibleToScripts { get; set; }
    }
}
