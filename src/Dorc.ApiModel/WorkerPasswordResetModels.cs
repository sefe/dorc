namespace Dorc.ApiModel
{
    /// <summary>
    /// Request body for the worker's password-reset endpoint (S-006). The worker
    /// impersonates its OWN service account (configured at install time per S-008) — never
    /// the original caller; CallerIdentity travels for audit logging only (HLPS D-3).
    /// </summary>
    public class WorkerPasswordResetRequestApiModel
    {
        public string ServerName { get; set; }
        public string Username { get; set; }
        public string CallerIdentity { get; set; }
    }
}
