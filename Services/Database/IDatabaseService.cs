namespace MugOverflowProject.Services.Database;

/// <summary>
/// Service interface marker and abstraction for high-performance SQLite database operations.
/// Extends ISqliteDatabaseService to provide full backwards and forwards compatibility across ORM implementations.
/// </summary>
public interface IDatabaseService : ISqliteDatabaseService
{
}
