using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public interface IUiPart
    {
        List<string> EnvironmentNames { set; get; }
        bool UserEditable { get; set; }
    }
}