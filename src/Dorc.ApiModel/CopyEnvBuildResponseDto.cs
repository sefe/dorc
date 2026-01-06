using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class CopyEnvBuildResponseDto
    {
        public List<int> RequestIds { get; set; }
        public string Message { get; set; }
        public bool Success { get; set; }
    }
}