using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class EnvironmentUiPartBase : IUiPart
    {
        public List<string> EnvironmentNames { get; set; }
        public bool UserEditable { get; set; }
    }
}