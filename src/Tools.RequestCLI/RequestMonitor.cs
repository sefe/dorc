using System;
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
        private readonly int _maxTransientFailures;
        private readonly TimeSpan _timeout;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly List<string> _goodStatuses = new() {"Completed"};
        private readonly List<string> _badStatuses = new() { "Errored", "Cancelled", "Failed", "Abandoned" };
        private readonly List<string> _validStatuses = new();

        public delegate void RequestStatusChanged(string message);
        public event RequestStatusChanged OnRequestStatusChanged;

        public RequestMonitor(
            IDorcApiClient api,
            int pollIntervalMilliseconds = 5000,
            int maxTransientFailures = 3,
            TimeSpan? timeout = null,
            Func<DateTimeOffset>? utcNow = null)
        {
            _api = api;
            _pollIntervalMilliseconds = pollIntervalMilliseconds;
            _maxTransientFailures = maxTransientFailures > 0 ? maxTransientFailures : 1;
            _timeout = timeout ?? TimeSpan.FromHours(1);
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _validStatuses.AddRange(_goodStatuses);
            _validStatuses.AddRange(_badStatuses);
        }

        public int MonitorRequest(int id)
        {
            var segments = new Dictionary<string, string> {{"id", $"{id}"}};
            var deadline = _utcNow() + _timeout;
            var consecutiveFailures = 0;
            string? lastKnownStatus = null;

            while (true)
            {
                var result = _api.Call<RequestStatusDto>(Endpoints.Request, Method.Get, segments);
                var status = result.IsModelValid ? result.Value?.Status : null;

                if (string.IsNullOrEmpty(status))
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= _maxTransientFailures)
                    {
                        return 2;
                    }
                }
                else
                {
                    consecutiveFailures = 0;
                    if (lastKnownStatus != null && OnRequestStatusChanged != null && lastKnownStatus != status)
                    {
                        OnRequestStatusChanged($"Request {id} changed status to {status}");
                    }

                    lastKnownStatus = status;
                    if (_validStatuses.Contains(status))
                    {
                        return _goodStatuses.Contains(status) ? 0 : 2;
                    }
                }

                if (_utcNow() >= deadline)
                {
                    return 2;
                }

                if (_pollIntervalMilliseconds > 0)
                {
                    Thread.Sleep(_pollIntervalMilliseconds);
                }
            }
        }
    }
}
