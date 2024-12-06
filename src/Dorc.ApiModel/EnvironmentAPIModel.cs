namespace Dorc.ApiModel
{
    public class EnvironmentApiModel
    {
        public int EnvironmentId { get; set; }
        public string EnvironmentName { get; set; }
        public bool EnvironmentSecure { get; set; }
        public bool EnvironmentIsProd { get; set; }
        public bool UserEditable { get; set; }
        public bool IsOwner { get; set; }
        public EnvironmentDetailsApiModel Details { get; set; }
    }
}