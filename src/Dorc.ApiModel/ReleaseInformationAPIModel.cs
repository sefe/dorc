using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class ReleaseInformationApiModel
    {
        public string Build;
        public IEnumerable<string> Components;
        public string Project;
    }
}