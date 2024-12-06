using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class TemplateApiModel<T>
    {
        public ProjectApiModel Project { get; set; }
        public IEnumerable<T> Items { get; set; }
    }
}