using Dorc.ApiModel;

namespace Dorc.Core.DatabaseAccess;

public interface IDatabaseAccessProvider
{
    string Name { get; }
    DatabaseAccessPreview Preview(DatabaseAccessTarget target, DatabaseAccessState desired);
    DatabaseAccessPreview Apply(DatabaseAccessTarget target, DatabaseAccessState desired, string token);
    List<string> SqlLogins(DatabaseAccessTarget target);
}
