namespace Dorc.Core
{
    public class ApiResult<T>
    {
        public T? Value { set; get; }
        public bool IsModelValid { set; get; }
        public string ErrorMessage { set; get; } = default!;
    }
}
