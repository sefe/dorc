using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class GetScopedPropertyValuesResponseDto
    {
        public int CurrentPage { get; set; }

        public int TotalItems { get; set; }

        public int TotalPages { get; set; }

        public List<FlatPropertyValueApiModel> Items { get; set; }
    }
}
