using MugOverflowProject.Models;
using MugOverflowProject.Services.Database;

namespace MugOverflowProject.Tests.Fixtures;

/// <summary>
/// In-memory mock SQLite database service for testing ViewModels without requiring disk I/O.
/// </summary>
public sealed class FakeSqliteDatabaseService : ISqliteDatabaseService
{
	public List<DecisionLogEntity> StoredEntities { get; } = [];
	public Dictionary<string, string> StoredMetadata { get; } = new();

	public int InitializeCallCount { get; private set; }
	public int InsertCallCount { get; private set; }
	public int UpdateCallCount { get; private set; }
	public int UpsertCallCount { get; private set; }
	public int DeleteCallCount { get; private set; }
	public int CloseCallCount { get; private set; }

	public Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		InitializeCallCount++;
		return Task.CompletedTask;
	}

	public Task<int> InsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		InsertCallCount++;
		ArgumentNullException.ThrowIfNull(entity);
		StoredEntities.Add(entity);
		return Task.FromResult(1);
	}

	public Task<int> UpdateLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		UpdateCallCount++;
		ArgumentNullException.ThrowIfNull(entity);
		int index = StoredEntities.FindIndex(e => e.Id == entity.Id);
		if (index >= 0)
		{
			StoredEntities[index] = entity;
			return Task.FromResult(1);
		}
		return Task.FromResult(0);
	}

	public Task<int> UpsertLogAsync(DecisionLogEntity entity, CancellationToken cancellationToken = default)
	{
		UpsertCallCount++;
		ArgumentNullException.ThrowIfNull(entity);
		int index = StoredEntities.FindIndex(e => e.Id == entity.Id);
		if (index >= 0)
		{
			StoredEntities[index] = entity;
		}
		else
		{
			StoredEntities.Add(entity);
		}
		return Task.FromResult(1);
	}

	public Task<int> DeleteLogAsync(string id, CancellationToken cancellationToken = default)
	{
		DeleteCallCount++;
		int removed = StoredEntities.RemoveAll(e => e.Id == id);
		return Task.FromResult(removed);
	}

	public Task<DecisionLogEntity?> GetLogByIdAsync(string id, CancellationToken cancellationToken = default)
	{
		var match = StoredEntities.FirstOrDefault(e => e.Id == id);
		return Task.FromResult(match);
	}

	public Task<List<DecisionLogEntity>> GetAllLogsAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(StoredEntities.OrderByDescending(e => e.Timestamp).ToList());
	}

	public Task<List<DecisionLogEntity>> GetPendingSyncLogsAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(StoredEntities.Where(e => e.SyncStatus == 0).ToList());
	}

	public Task<int> MarkLogsSyncedAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
	{
		var idSet = ids.ToHashSet();
		int count = 0;
		foreach (var entity in StoredEntities)
		{
			if (idSet.Contains(entity.Id))
			{
				entity.SyncStatus = 1;
				count++;
			}
		}
		return Task.FromResult(count);
	}

	public Task<int> MarkAllLogsSyncedAsync(CancellationToken cancellationToken = default)
	{
		int count = 0;
		foreach (var entity in StoredEntities)
		{
			if (entity.SyncStatus == 0)
			{
				entity.SyncStatus = 1;
				count++;
			}
		}
		return Task.FromResult(count);
	}

	public Task<int> UpsertBatchAsync(IEnumerable<DecisionLogEntity> entities, CancellationToken cancellationToken = default)
	{
		int count = 0;
		foreach (var entity in entities)
		{
			int index = StoredEntities.FindIndex(e => e.Id == entity.Id);
			if (index >= 0)
			{
				StoredEntities[index] = entity;
			}
			else
			{
				StoredEntities.Add(entity);
			}
			count++;
		}
		return Task.FromResult(count);
	}

	public Task<int> ClearAllLogsAsync(CancellationToken cancellationToken = default)
	{
		int count = StoredEntities.Count;
		StoredEntities.Clear();
		return Task.FromResult(count);
	}

	public Task<string> GetMetadataAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
	{
		return Task.FromResult(StoredMetadata.TryGetValue(key, out var val) ? val : defaultValue);
	}

	public Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
	{
		StoredMetadata[key] = value;
		return Task.CompletedTask;
	}

	public Task<string> GetJournalModeAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult("wal");
	}

	public Task CloseConnectionAsync()
	{
		CloseCallCount++;
		return Task.CompletedTask;
	}
}
