namespace Dorc.Api.Interfaces
{
    public interface IServiceNowService
    {
        Task<ChangeRequestValidationResult> ValidateChangeRequestAsync(string crNumber);
        Task<CreateChangeRequestResult> CreateChangeRequestAsync(CreateChangeRequestInput input);
    }

    public class CreateChangeRequestInput
    {
        public string ProjectName { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string BuildNumber { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string AssignmentGroup { get; set; } = string.Empty;
        public string BusinessService { get; set; } = string.Empty;
        public string ChgModel { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string BackoutPlan { get; set; } = string.Empty;
        public string ImplementationPlan { get; set; } = string.Empty;
        public string Justification { get; set; } = string.Empty;
        public string TestPlan { get; set; } = string.Empty;
        public string RiskImpactAnalysis { get; set; } = string.Empty;
        public string WorkNotes { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Urgency { get; set; } = string.Empty;
        public bool CrInputsFetched { get; set; }
    }

    public class CreateChangeRequestResult
    {
        public bool Success { get; set; }
        public string CrNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ChangeRequestValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string CrNumber { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
    }
}
