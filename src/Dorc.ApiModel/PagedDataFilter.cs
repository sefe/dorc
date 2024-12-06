using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public class PagedDataOperators
    {
        public List<PagedDataFilter> Filters { get; set; }
        public List<PagedDataSorting> SortOrders { get; set; }
    }

    public class PagedDataFilter
    {
        public string Path { get; set; }
        public string FilterValue { get; set; }
    }

    public class PagedDataSorting
    {
        public string Path { get; set; }
        public string Direction { get; set; }
    }
}