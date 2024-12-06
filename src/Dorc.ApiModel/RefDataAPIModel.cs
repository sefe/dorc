using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class RefDataApiModel
    {
        public ProjectApiModel Project { get; set; }

        public IList<ComponentApiModel> Components { get; set; }
    }
}