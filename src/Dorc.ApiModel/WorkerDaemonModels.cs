using System.Collections.Generic;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Credential the worker impersonates (via LogonUser) for daemon service-control
    /// operations. These are the environment-specific DORC deploy accounts resolved by the
    /// primary from config values — they are per-environment data, so they cannot be
    /// provisioned into the worker's own config at install time and travel per-request
    /// instead. The loopback bind plus X-Worker-Key are the transport boundary (HLPS D-1/D-3).
    /// </summary>
    public class WorkerDaemonCredentialApiModel
    {
        public string Username { get; set; }
        public string Domain { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// One daemon on one server, as exchanged with the Windows worker. Mirrors
    /// Dorc.Core.DaemonStatus; ServerId/DaemonId are correlation pass-throughs so the
    /// primary can record observation rows for probed results — the worker never touches
    /// the database.
    /// </summary>
    public class WorkerDaemonApiModel
    {
        public string EnvName { get; set; }
        public string ServerName { get; set; }
        public string DaemonName { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public int? ServerId { get; set; }
        public int? DaemonId { get; set; }
    }

    /// <summary>Request body for the worker's daemons/probe endpoint (S-005).</summary>
    public class WorkerDaemonProbeRequestApiModel
    {
        /// <summary>Null probes without impersonation (the discovery path when no deploy credential is configured).</summary>
        public WorkerDaemonCredentialApiModel Credential { get; set; }

        public List<WorkerDaemonApiModel> Daemons { get; set; } = new List<WorkerDaemonApiModel>();
    }

    /// <summary>
    /// Request body for the worker's daemons/change-state endpoint (S-005).
    /// Daemon.Status carries the requested action: Starting, Stopping or Restarting.
    /// </summary>
    public class WorkerDaemonStateChangeRequestApiModel
    {
        public WorkerDaemonCredentialApiModel Credential { get; set; }

        public WorkerDaemonApiModel Daemon { get; set; } = new WorkerDaemonApiModel();
    }
}
