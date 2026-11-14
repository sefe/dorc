namespace Dorc.PersistentData.Model
{
    public class ConfigValue
    {
        public int Id { get; set; }
        public string Key { get; set; } = null!;
        public string? Value { get; set; }
        public bool Secure { get; set; }
        public bool? IsForProd { get; set; }

        /// <summary>
        /// Whether this value may be resolved into deployment script scope. Existing values
        /// default to visible so nothing changes behaviour on upgrade; new secure values are
        /// created hidden.
        /// </summary>
        public bool VisibleToScripts { get; set; } = true;
    }
}