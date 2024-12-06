using System;

namespace Dorc.ApiModel
{
    public class PropertyValueAuditApiModel
    {
        public long Id { get; set; }
        public long? PropertyId { get; set; }
        public long? PropertyValueId { get; set; }
        public string PropertyName { get; set; }
        public string EnvironmentName { get; set; }
        public virtual string FromValue { get; set; }
        public virtual string ToValue { get; set; }
        public string UpdatedBy { get; set; }
        public DateTime UpdatedDate { get; set; }
        public string Type { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool IsEnabled { set; get; }
    }
}