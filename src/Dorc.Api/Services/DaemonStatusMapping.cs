using Dorc.ApiModel;
using Dorc.Core;

namespace Dorc.Api.Services
{
    public static class DaemonStatusMapping
    {
        public static DaemonStatusApiModel ToApi(DaemonStatus d) =>
            new DaemonStatusApiModel
            {
                EnvName = d.EnvName,
                ServerName = d.ServerName,
                DaemonName = d.DaemonName,
                Status = d.Status,
                ErrorMessage = d.ErrorMessage
            };

        public static DaemonStatus ToCore(DaemonStatusApiModel m) =>
            new DaemonStatus
            {
                EnvName = m.EnvName,
                ServerName = m.ServerName,
                DaemonName = m.DaemonName,
                Status = m.Status,
                ErrorMessage = m.ErrorMessage
            };
    }
}
