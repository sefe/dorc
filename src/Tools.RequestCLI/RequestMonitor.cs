using System.Collections.Generic;
using System.Threading;
using Dorc.ApiModel;
using Dorc.Core;
using RestSharp;

namespace Tools.RequestCLI
{
    public class RequestMonitor
    {
        private readonly IApiCaller _api;
        private readonly List<string> _goodStatuses = new() {"Completed"};
        private readonly List<string> _badStatuses = new() { "Errored", "Cancelled", "Failed" };
        private readonly List<string> _validStatuses = new();

        public delegate void RequestStatusChanged(string message);
        public event RequestStatusChanged OnRequestStatusChanged;

        public RequestMonitor(IApiCaller api)
        {
            _api = api;
            _validStatuses.AddRange(_goodStatuses);
            _validStatuses.AddRange(_badStatuses);
        }

        public int MonitorRequest(int id)
        {
            var oldStatus = "";
            var segments = new Dictionary<string, string> {{"id", $"{id}"}};
            var result = _api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, segments);
            var status = result.IsModelValid ? result.Value.Status : null;
            while (status !=null && !_validStatuses.Contains(status))
            {
                result = _api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, segments);
                status = result.IsModelValid ? result.Value.Status : null;
                if (OnRequestStatusChanged != null && oldStatus!=status)
                {
                    OnRequestStatusChanged($"Request {id} changed status to {status}");
                }
                oldStatus = status;
                Thread.Sleep(5000);
            }

            return _goodStatuses.Contains(oldStatus) ? 0 : 2;
        }
    }
}
