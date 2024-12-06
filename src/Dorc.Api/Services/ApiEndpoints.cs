namespace Dorc.Api.Services
{
    public class ApiEndpoints
    {
        public string Property { get; }
        public string PropertyValues { get; }
        public string Root { get; }
        public string Request { get; }

        public ApiEndpoints(HttpRequest request)
        {
            Root = $"{request.Scheme}://{request.Host}";
            Property = Root + "/Properties";
            PropertyValues = Root + "/PropertyValues";
            Request = Root + "/Request";
        }
    }
}