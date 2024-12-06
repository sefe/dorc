namespace Tests.Acceptance.Support
{
    public class ApiResult<T>
    {
        public string? RawJson { set; get; }
        public object? Model { set; get; }
        public bool IsModelValid { set; get; }
        public string? Message { set; get; }
    }
}
