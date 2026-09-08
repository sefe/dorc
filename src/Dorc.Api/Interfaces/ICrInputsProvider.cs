namespace Dorc.Api.Interfaces
{
    public interface ICrInputsProvider
    {
        Task<CrInputsModel?> GetCrInputsAsync(string projectName);
    }

    /// <summary>Maps to a team's cr-inputs.json (sefe/service-now-cli schema). All fields optional.</summary>
    public class CrInputsModel
    {
        public string AssignmentGroup { get; set; } = string.Empty;
        public string BusinessService { get; set; } = string.Empty;
        public string ChgModel { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
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
        public string ScheduledStartDate { get; set; } = string.Empty;
        public string ScheduledEndDate { get; set; } = string.Empty;
        public string BranchingStrategy { get; set; } = string.Empty;
        public string TeamProjectName { get; set; } = string.Empty;
        public string IssuePathFilter { get; set; } = string.Empty;
    }
}
