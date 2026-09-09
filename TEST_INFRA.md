# E2E Test Infra: MugOverflow BYOS Cloud Synchronization and SQLite Persistence

## Test Philosophy
- **Requirement-Driven & Opaque-Box**: Tests are derived strictly from `ORIGINAL_REQUEST.md`, `project_requirements.md`, and `master_todo_list.md`.
- **Zero Flakiness**: All test fixtures use isolated in-memory or temporary `.db3` files with clean teardown, and HTTP/cloud fakes for zero external network dependency.
- **Deterministic Verification**: Tests verify state transitions, LWW timestamps, transactional atomicity, offline queueing, and UI disclaimer contracts.

## Feature Inventory
| # | Feature | Source (Requirement) | Tier 1 (Coverage) | Tier 2 (Boundary/Corner) | Tier 3 (Cross-Feature) |
|---|---------|----------------------|:-----------------:|:------------------------:|:----------------------:|
| 1 | SQLite WAL & Entities | ORIGINAL_REQUEST §R1 | ≥ 5 tests | ≥ 5 tests | ✓ |
| 2 | Storage Providers & Local | ORIGINAL_REQUEST §R2 | ≥ 5 tests | ≥ 5 tests | ✓ |
| 3 | Cloud Providers (Google/OneDrive) | ORIGINAL_REQUEST §R2 | ≥ 5 tests | ≥ 5 tests | ✓ |
| 4 | Two-Way Union Hydration & LWW | ORIGINAL_REQUEST §R3 | ≥ 5 tests | ≥ 5 tests | ✓ |
| 5 | Periodic Flush & Lifecycle Sync | ORIGINAL_REQUEST §R3 | ≥ 5 tests | ≥ 5 tests | ✓ |
| 6 | UI, Disclaimer & Migration | ORIGINAL_REQUEST §R4 | ≥ 5 tests | ≥ 5 tests | ✓ |

## Test Architecture
- **Runner**: `dotnet test tests/MugOverflowProject.Tests/MugOverflowProject.Tests.csproj`
- **Framework**: xUnit (`net10.0`) + Coverlet
- **Fixtures**:
  - `SqliteTestDatabaseFixture`: Creates temporary isolated GUID-named `.db3` databases, validates WAL mode, tests transactions, and ensures `CloseConnectionAsync` prior to file cleanup.
  - `FakeStorageProvider`: In-memory BYOS provider with payload tracking, latency simulation, and network error injection.
  - `FakeConnectivity`: Simulates `NetworkAccess.Internet`, `NetworkAccess.None`, and connectivity change events.
  - `FakeSecureStorage`: In-memory token store for OAuth refresh tokens.

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Multi-Device Sync Convergence | F1, F2, F4, F5 | High |
| 2 | Offline Mutation Queue & Automatic Reconnection Flush | F1, F4, F5 | High |
| 3 | Provider Switching with 3-Way Migration (Local -> Google -> OneDrive) | F1, F2, F3, F4, F6 | High |
| 4 | Sudden App Backgrounding / Sleep Lifecycle Flush | F1, F4, F5 | Medium |
| 5 | Rapid Concurrent Decision Logging with Transactional Batch Integrity | F1, F5 | High |

## Coverage Thresholds
- **Tier 1 (Feature Coverage)**: ≥ 30 test cases (≥ 5 per feature across 6 features)
- **Tier 2 (Boundary & Corner)**: ≥ 30 test cases (corrupt JSON, empty payloads, duplicate GUIDs, disjoint timestamp conflicts, connection drops)
- **Tier 3 (Cross-Feature Combinations)**: ≥ 10 test cases (pairwise provider switching + hydration + offline transitions)
- **Tier 4 (Real-World Application Scenarios)**: ≥ 5 end-to-end integration scenarios
- **Tier 5 (Adversarial Coverage Hardening)**: White-box adversarial testing by Challenger agent
