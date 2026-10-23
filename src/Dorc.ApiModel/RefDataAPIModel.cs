using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class RefDataApiModel
    {
        public string Revision { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDeploy { get; set; }
        public ProjectApiModel Project { get; set; }

        public IList<ComponentApiModel> Components { get; set; }
    }
}