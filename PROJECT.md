# Project: MugOverflow BYOS Cloud Synchronization and SQLite Persistence

## Architecture
MugOverflow is a .NET 10 MAUI application that provides caffeine tracking, metabolism modeling, and compliance insights.
This project introduces a dual-layer storage architecture:
1. **Local Relational Persistence**: Low-latency, in-process SQLite storage using `sqlite-net-pcl` (1.11.*) and `SQLitePCLRaw.bundle_green` (2.1.*) with Write-Ahead Logging (WAL) mode enabled (`PRAGMA journal_mode=WAL;`).
2. **Bring Your Own Storage (BYOS) Cloud Sync**: Pluggable storage providers (`IStorageProvider`) supporting Local Storage (`FileSystem.AppDataDirectory/MugOverflow-Caffeine-consumption/`), Google Drive (zero-backend OAuth 2.0 PKCE via `WebAuthenticator`), and Microsoft OneDrive (MSAL.NET via Microsoft Graph v1.0 `special/approot`).
3. **Synchronization & Conflict Resolution**: A unified `StorageSyncService` performing cold-start two-way union hydration with Last-Write-Wins (LWW) resolution based on `LastModifiedUtc`, a 5-minute background periodic flush, proactive lifecycle sync on `App.OnSleep` / `Window.Destroying`, and offline resilience.
4. **User Experience & Provider Switching**: Storage selection popup with mandatory Local Storage isolation disclaimer, 3-way migration workflow (Upload local to cloud, Download cloud to local, Two-way merge both), and real-time header sync/offline status badge.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | SQLite Persistence & Entities | `sqlite-net-pcl` & `SQLitePCLRaw.bundle_green`, WAL mode, `DecisionLogEntity`, `SyncMetadataEntity`, CRUD/batch transactions | M1 | ORIGINAL_REQUEST §R1 |
| 2 | Storage Provider Core & Local | `IStorageProvider` contract, `StoragePayload`, `LocalStorageProvider` file manager | M2 | ORIGINAL_REQUEST §R2 |
| 3 | Cloud Providers (Google & OneDrive) | `GoogleDriveStorageProvider` (PKCE), `OneDriveStorageProvider` (MSAL/Graph), Secure Storage integration | M2 | ORIGINAL_REQUEST §R2 |
| 4 | Two-Way Union Hydration & LWW | Cold-start two-way union merge inside `RunInTransactionAsync`, LWW timestamp resolution | M3 | ORIGINAL_REQUEST §R3 |
| 5 | Periodic Flush & Lifecycle Sync | 5-minute timer, `App.OnSleep`/`Window.Destroying` triggers, offline dirty tracking, connectivity reaction | M3 | ORIGINAL_REQUEST §R3 |
| 6 | UI, Migration & Status Indication | `StorageSelectionPopup`, isolation warning disclaimer, 3-way migration dialog, header sync status badge | M4 | ORIGINAL_REQUEST §R4 |
| 7 | Full E2E & Unit Test Coverage | Unit test suites for SQLite, Providers, Sync, Migration, and Tiers 1-5 E2E validation | M5 / Test Track | ORIGINAL_REQUEST Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | SQLite Persistence Layer | Package references, `DecisionLogEntity`, `SyncMetadataEntity`, `SqliteDatabaseService`, WAL initialization, transactional batch ops, unit tests | None | DONE |
| M2 | Storage Providers & Auth | `IStorageProvider`, `StoragePayload`, `LocalStorageProvider`, `GoogleDriveStorageProvider`, `OneDriveStorageProvider`, platform redirect configs, token storage, unit tests | M1 Interface Contracts | DONE |
| M3 | Sync Orchestrator & Hydration | `StorageSyncService`, cold-start union hydration, LWW conflict resolution, 5-min periodic flush, lifecycle hooks (`App.OnSleep`, `Window.Destroying`), offline queue, unit tests | M1, M2 | DONE |
| M4 | UI & Provider Migration | `StorageSelectionPopup`, isolation disclaimer, migration dialog, status badge in `MainPage`, `MainViewModel` integration | M1, M2, M3 | DONE |
| M5 | E2E Pass & Adversarial Hardening | Pass 100% E2E test suite (Tiers 1-4), adversarial test coverage hardening (Tier 5), Forensic Audit verification | M1, M2, M3, M4, Test Track | DONE |

## Interface Contracts

### `Models/DecisionLogEntity.cs` & `Models/SyncMetadataEntity.cs`
- `DecisionLogEntity`:
  - `[PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString();`
  - `[Indexed] public DateTime Timestamp { get; set; }`
  - `public DateTime LastModifiedUtc { get; set; }`
  - `public string Decision { get; set; } = string.Empty;`
  - `public string Reason { get; set; } = string.Empty;`
  - `public double CaffeineAmountMg { get; set; }`
  - `public string ItemName { get; set; } = string.Empty;`
  - `public string ItemCategory { get; set; } = string.Empty;`
  - `public bool IsComplianceOverride { get; set; }`
  - `[Indexed] public int SyncStatus { get; set; }` (0 = Pending, 1 = Synced, 2 = ConflictResolved)
- `SyncMetadataEntity`:
  - `[PrimaryKey] public string Key { get; set; } = string.Empty;`
  - `public string Value { get; set; } = string.Empty;`
  - `public DateTime LastUpdatedUtc { get; set; }`

### `Services/ISqliteDatabaseService.cs`
```csharp
public interface ISqliteDatabaseService
{
    Task InitializeAsync();
    Task<int> InsertLogAsync(DecisionLogEntity entity);
    Task<int> UpdateLogAsync(DecisionLogEntity entity);
    Task<int> DeleteLogAsync(string id);
    Task<DecisionLogEntity?> GetLogByIdAsync(string id);
    Task<List<DecisionLogEntity>> GetAllLogsAsync();
    Task<List<DecisionLogEntity>> GetPendingSyncLogsAsync();
    Task<int> MarkLogsSyncedAsync(IEnumerable<string> ids);
    Task<int> UpsertBatchAsync(IEnumerable<DecisionLogEntity> entities);
    Task<string> GetMetadataAsync(string key, string defaultValue = "");
    Task SetMetadataAsync(string key, string value);
    Task CloseConnectionAsync();
}
```

### `Services/Storage/IStorageProvider.cs`
```csharp
public enum StorageProviderType
{
    LocalStorage,
    GoogleDrive,
    OneDrive
}

public class StoragePayload
{
    public string AppVersion { get; set; } = "1.0.0";
    public string SchemaVersion { get; set; } = "1.0.0";
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DecisionLogEntity> DecisionLogs { get; set; } = new();
}

public interface IStorageProvider
{
    StorageProviderType ProviderType { get; }
    string DisplayName { get; }
    bool RequiresAuthentication { get; }
    Task<bool> IsAuthenticatedAsync();
    Task<bool> AuthenticateAsync();
    Task SignOutAsync();
    Task<StoragePayload?> ReadPayloadAsync();
    Task<bool> WritePayloadAsync(StoragePayload payload);
    Task<bool> TestConnectionAsync();
}
```

### `Services/Sync/IStorageSyncService.cs`
```csharp
public enum SyncEngineStatus
{
    Idle,
    Syncing,
    Offline,
    Error,
    Success
}

public enum MigrationOption
{
    UploadLocalToCloud,
    DownloadCloudToLocal,
    TwoWayMergeBoth
}

public interface IStorageSyncService
{
    SyncEngineStatus Status { get; }
    DateTime? LastSuccessfulSyncUtc { get; }
    IStorageProvider CurrentProvider { get; }
    event EventHandler<SyncEngineStatus>? StatusChanged;
    
    Task InitializeAsync();
    Task<bool> SynchronizeAsync(CancellationToken ct = default);
    Task<bool> FlushPendingChangesAsync(CancellationToken ct = default);
    Task<bool> SwitchProviderAsync(StorageProviderType targetType, MigrationOption migrationOption, CancellationToken ct = default);
    Task HandleAppSleepAsync();
    Task HandleAppDestroyAsync();
}
```

## Code Layout
```
MugOverflowProject/
├── Models/
│   ├── DecisionLogEntity.cs
│   ├── SyncMetadataEntity.cs
│   └── StoragePayload.cs
├── Services/
│   ├── Database/
│   │   ├── ISqliteDatabaseService.cs
│   │   └── SqliteDatabaseService.cs
│   ├── Storage/
│   │   ├── IStorageProvider.cs
│   │   ├── LocalStorageProvider.cs
│   │   ├── GoogleDriveStorageProvider.cs
│   │   └── OneDriveStorageProvider.cs
│   ├── Sync/
│   │   ├── IStorageSyncService.cs
│   │   └── StorageSyncService.cs
│   └── ... (existing services)
├── ViewModels/
│   ├── StorageSelectionViewModel.cs
│   └── MainViewModel.cs (updated with sync integration)
├── Views/
│   ├── StorageSelectionPopup.xaml
│   ├── StorageSelectionPopup.xaml.cs
│   └── MainPage.xaml (header sync status badge)
├── Platforms/
│   ├── Android/MsalActivity.cs & AndroidManifest.xml
│   ├── iOS/Info.plist & AppDelegate.cs
│   └── Windows/
tests/MugOverflowProject.Tests/
├── Fixtures/
│   ├── SqliteTestDatabaseFixture.cs
│   ├── FakeStorageProvider.cs
│   └── FakeConnectivity.cs
├── Database/
│   └── SqliteDatabaseServiceTests.cs
├── Storage/
│   ├── LocalStorageProviderTests.cs
│   ├── GoogleDriveStorageProviderTests.cs
│   └── OneDriveStorageProviderTests.cs
├── Sync/
│   ├── StorageSyncServiceTests.cs
│   └── TwoWayHydrationTests.cs
└── ViewModels/
    └── StorageSelectionViewModelTests.cs
```
