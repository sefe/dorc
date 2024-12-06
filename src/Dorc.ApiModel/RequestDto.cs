using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class RequestDto
    {
        public RequestDto()
        {
            RequestProperties = new List<RequestProperty>();
        }

        public string Project { set; get; }
        public string Environment { set; get; }
        public string BuildUrl { set; get; }
        public string BuildText { set; get; }
        public string BuildNum { set; get; }
        public ICollection<string> Components { set; get; }
        public bool? Pinned { set; get; }
        public ICollection<RequestProperty> RequestProperties { set; get; }
        public string VstsUrl { set; get; }

        public override string ToString()
        {
            return
                $"Project: {Project} Environment: {Environment} BuildUrl: {BuildUrl} BuildText: {BuildText} BuildNum: {BuildNum} Pinned:{Pinned} VstsUrl:{VstsUrl}";
        }
    }
}