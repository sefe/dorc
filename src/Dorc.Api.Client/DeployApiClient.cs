using Serilog;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace Dorc.Api.Client
{
    public class DeployApiClient : IDeployApiClient
    {
        private string _baseUrl;
        public string urlParams { get; set; }

        public DeployApiClient(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public async void PostToDorc(ILogger contextlogger, string endPoint, string urlParams)
        {
            HttpClientHandler handler = new HttpClientHandler { UseDefaultCredentials = true };
            string fullUrl = $"{_baseUrl}/{endPoint}?{urlParams}";
            contextlogger.Verbose($"full url {fullUrl}");

            try
            {
                using (HttpClient client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Add("Accept", "*/*");
                    HttpContent content = new StringContent("");
                    var response = await client.PostAsync(fullUrl, content).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        contextlogger.LogError($"Request failed with status code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception e)
            {
                contextlogger.LogError($"Post to {fullUrl} failed with Exception {e.Message}");
            }
        }

        public async void PatchToDorc(ILogger contextlogger, string endPoint, string patchContent)
        {
            string fullUrl = $"{_baseUrl}/{endPoint}?{urlParams}";
            contextlogger.Verbose($"full url {fullUrl}");
            HttpClientHandler handler = new HttpClientHandler { UseDefaultCredentials = true };
            using (HttpClient client = new HttpClient(handler))
            {
                try
                {
                    // Add the 'accept: */*' header
                    client.DefaultRequestHeaders.Add("Accept", "*/*");
                    //Wrap the log entry with quotes
                    var jsonBody = JsonConvert.ToString(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + patchContent);
                    HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PATCH"), fullUrl)
                    {
                        Content = content
                    };
                    var response = client.SendAsync(request).Result;
                    response.EnsureSuccessStatusCode();
                    if (!response.IsSuccessStatusCode)
                    {
                        contextlogger.LogError($"Request to {fullUrl} failed with status code: {response.StatusCode}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    contextlogger.LogError($"Post to {fullUrl} failed with Exception {ex.Message}");
                }
            }
        }
    }
}
