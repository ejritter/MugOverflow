using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Database;

/// <summary>
/// Thread-safe SQLite database service managing local persistence for MugOverflow.
/// Standardized 100% on Dapper + Microsoft.Data.Sqlite, inheriting from <see cref="DapperDatabaseService"/>
/// to provide high-performance WAL mode persistence, connection pooling, and full backwards compatibility.
/// </summary>
public sealed class SqliteDatabaseService : DapperDatabaseService
{
	/// <summary>
	/// Default constructor utilizing standard MAUI app data directory.
	/// </summary>
	public SqliteDatabaseService()
		: base()
	{
	}

	/// <summary>
	/// Constructor accepting custom database path (for isolated testing or custom locations).
	/// </summary>
	public SqliteDatabaseService(string databasePath)
		: base(databasePath)
	{
	}
}

