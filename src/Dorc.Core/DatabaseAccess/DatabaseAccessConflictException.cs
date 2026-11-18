namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessConflictException(string message) : Exception(message);
