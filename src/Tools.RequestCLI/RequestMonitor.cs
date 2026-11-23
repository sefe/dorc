using System.Collections.Generic;
using System.Threading;
using Dorc.ApiModel;
using Dorc.Core;
using RestSharp;

namespace Tools.RequestCLI
{
    public class RequestMonitor
    {
        private readonly IDorcApiClient _api;
        private readonly int _pollIntervalMilliseconds;
        private readonly List<string> _goodStatuses = new() {"Completed"};
        private readonly List<string> _badStatuses = new() { "Errored", "Cancelled", "Failed", "Abandoned" };
        private readonly List<string> _validStatuses = new();

        public delegate void RequestStatusChanged(string message);
        public event RequestStatusChanged OnRequestStatusChanged;

        public RequestMonitor(IDorcApiClient api, int pollIntervalMilliseconds = 5000)
        {
            _api = api;
            _pollIntervalMilliseconds = pollIntervalMilliseconds;
            _validStatuses.AddRange(_goodStatuses);
            _validStatuses.AddRange(_badStatuses);
        }

        public int MonitorRequest(int id)
        {
            var segments = new Dictionary<string, string> {{"id", $"{id}"}};
            var result = _api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, segments);
            var status = result.IsModelValid ? result.Value.Status : null;
            var oldStatus = status ?? "";
            while (status !=null && !_validStatuses.Contains(status))
            {
                result = _api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, segments);
                status = result.IsModelValid ? result.Value.Status : null;
                if (OnRequestStatusChanged != null && oldStatus!=status)
                {
                    OnRequestStatusChanged($"Request {id} changed status to {status}");
                }
                oldStatus = status;
                if (_pollIntervalMilliseconds > 0)
                {
                    Thread.Sleep(_pollIntervalMilliseconds);
                }
            }

            return _goodStatuses.Contains(oldStatus ?? "") ? 0 : 2;
        }
    }
}
