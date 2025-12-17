using System.ComponentModel.DataAnnotations;

namespace Dorc.PersistentData.Model
{
    public class SecurityObject : IEquatable<SecurityObject>
    {
        [ScaffoldColumn(false)] public virtual Guid ObjectId { get; set; }

        public virtual string Name { get; set; } = default!;

        public bool Equals(SecurityObject? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Name, other.Name);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != typeof(SecurityObject)) return false;
            return Equals((SecurityObject)obj);
        }

        public override int GetHashCode()
        {
            return Name != null ? Name.GetHashCode() : 0;
        }

        public static bool operator ==(SecurityObject left, SecurityObject right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SecurityObject? left, SecurityObject right)
        {
            return !Equals(left, right);
        }
    }
}