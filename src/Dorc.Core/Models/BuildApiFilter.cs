using Org.OpenAPITools.Model;

namespace Dorc.Core.Models
{
    public class BuildApiFilter
    {
        public Build.StatusEnum? Status { get; set; }
        public Build.ResultEnum? Result { get; set; }

        public BuildApiFilter(Build.StatusEnum? status, Build.ResultEnum? result)
        {
            Status = status;
            Result = result;
        }
    }
}
