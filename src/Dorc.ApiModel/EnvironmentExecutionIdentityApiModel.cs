namespace Dorc.ApiModel
{
    public class EnvironmentExecutionIdentityApiModel
    {
        /// <summary>
        /// The named deployment identity to bind, or null/empty to restore the tier default.
        /// </summary>
        public string ExecutionIdentityReference { get; set; }
    }
}
