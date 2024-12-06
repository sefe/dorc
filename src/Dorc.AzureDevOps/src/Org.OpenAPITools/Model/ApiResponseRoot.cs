using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Org.OpenAPITools.Model
{
    [DataContract(Name = "ApiResponseRoot")]
    public class ApiResponseRoot<T>
    {
        [DataMember(Name = "count", EmitDefaultValue = false)]
        public int Count { get; set; }

        [DataMember(Name = "value", EmitDefaultValue = false)]
        public T Value { get; set; }
    }
}
