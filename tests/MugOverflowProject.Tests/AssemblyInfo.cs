using Xunit;

// Enforce sequential execution of test collections across the entire assembly.
// This prevents native SQLite C runtime collisions, GC finalizer race conditions on Windows,
// and temp file lock contention during full solution test runs.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
