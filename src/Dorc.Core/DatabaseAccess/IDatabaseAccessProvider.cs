using Dorc.ApiModel;

namespace Dorc.Core.DatabaseAccess;

public record DatabaseAccessTarget(int Id, string Server, string Database);

public interface IDatabaseAccessProvider
{
    string Name { get; }
    DatabaseAccessPreview Preview(DatabaseAccessTarget target, DatabaseAccessState desired);
    DatabaseAccessPreview Apply(DatabaseAccessTarget target, DatabaseAccessState desired, string token);
}
