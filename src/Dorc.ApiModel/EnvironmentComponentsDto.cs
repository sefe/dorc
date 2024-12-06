using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class EnvironmentComponentsDto<T>
    {
        public List<T> Result { set; get; }
    }
}