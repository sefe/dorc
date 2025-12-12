using Dorc.ApiModel;

namespace Dorc.PersistentData.Model
{
    public class Component : SecurityObject
    {
        public int Id { get; set; }
        public string? Description { get; set; }
        public Component? Parent { get; set; }
        public bool IsEnabled { set; get; }
        public bool StopOnFailure { get; set; }
        public Script? Script { get; set; }
        public int? ScriptId { get; set; }

        public ComponentType ComponentType { get; set; } = ComponentType.PowerShell;
        public ICollection<Component> Children { get; set; } = new List<Component>();
        public ICollection<Project> Projects { get; set; } = new List<Project>();
    }
}