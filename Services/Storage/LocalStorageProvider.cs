using Microsoft.Extensions.Logging;
using MugOverflowProject.Models;

namespace MugOverflowProject.Services.Storage;

/// <summary>
/// Provides atomic local file system persistence for caffeine consumption payloads within
/// FileSystem.AppDataDirectory/MugOverflow-Caffeine-consumption/caffeine_consumption.json.
/// </summary>
public sealed class LocalStorageProvider : IStorageProvider
{
	public const string SubFolderName = "MugOverflow-Caffeine-consumption";
	public const string FileName = "caffeine_consumption.json";

	private readonly string _baseDirectory;
	private readonly ILogger<LocalStorageProvider>? _logger;
	private readonly SemaphoreSlim _fileLock = new(1, 1);

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalStorageProvider"/> class.
	/// </summary>
	/// <param name="baseDirectory">Optional base directory override (defaults to FileSystem.AppDataDirectory).</param>
	/// <param name="logger">Optional logger instance.</param>
	public LocalStorageProvider(string? baseDirectory = null, ILogger<LocalStorageProvider>? logger = null)
	{
		_baseDirectory = !string.IsNullOrWhiteSpace(baseDirectory)
			? baseDirectory
			: FileSystem.AppDataDirectory;
		_logger = logger;
	}

	/// <inheritdoc />
	public StorageProviderType ProviderType => StorageProviderType.LocalStorage;

	/// <inheritdoc />
	public string DisplayName => "Local Storage";

	/// <inheritdoc />
	public bool RequiresAuthentication => false;

	/// <summary>
	/// Gets the absolute target folder path where the payload is stored.
	/// </summary>
	public string TargetDirectory => Path.Combine(_baseDirectory, SubFolderName);

	/// <summary>
	/// Gets the absolute file path to caffeine_consumption.json.
	/// </summary>
	public string FilePath => Path.Combine(TargetDirectory, FileName);

	/// <inheritdoc />
	public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<StoragePayload?> ReadPayloadAsync(CancellationToken cancellationToken = default)
	{
		await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!Directory.Exists(TargetDirectory) || !File.Exists(FilePath))
			{
				_logger?.LogDebug("Local storage payload file does not exist at {FilePath}. Returning null.", FilePath);
				return null;
			}

			string json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(json))
			{
				_logger?.LogWarning("Local storage payload file at {FilePath} is empty.", FilePath);
				return null;
			}

			var payload = StoragePayload.FromJson(json);
			if (payload == null)
			{
				_logger?.LogWarning("Local storage payload at {FilePath} could not be deserialized.", FilePath);
				return null;
			}

			return payload;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error reading local storage payload from {FilePath}", FilePath);
			return null;
		}
		finally
		{
			_fileLock.Release();
		}
	}

	/// <inheritdoc />
	public async Task<bool> WritePayloadAsync(StoragePayload payload, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(payload);

		await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		string tempFilePath = Path.Combine(TargetDirectory, $"{FileName}.{Guid.NewGuid():N}.tmp");

		try
		{
			if (!Directory.Exists(TargetDirectory))
			{
				Directory.CreateDirectory(TargetDirectory);
			}

			string json = payload.ToJson();
			await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);

			const int maxRetries = 3;
			const int retryDelayMs = 50;
			bool replaced = false;

			for (int attempt = 1; attempt <= maxRetries; attempt++)
			{
				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					File.Move(tempFilePath, FilePath, overwrite: true);
					replaced = true;
					break;
				}
				catch (IOException ex) when (attempt < maxRetries)
				{
					_logger?.LogWarning(ex, "Transient IO lock on {FilePath}. Retrying ({Attempt}/{MaxRetries})...", FilePath, attempt, maxRetries);
					await Task.Delay(retryDelayMs * attempt, cancellationToken).ConfigureAwait(false);
				}
				catch (UnauthorizedAccessException ex) when (attempt < maxRetries)
				{
					_logger?.LogWarning(ex, "Transient security lock on {FilePath}. Retrying ({Attempt}/{MaxRetries})...", FilePath, attempt, maxRetries);
					await Task.Delay(retryDelayMs * attempt, cancellationToken).ConfigureAwait(false);
				}
			}

			if (!replaced)
			{
				_logger?.LogError("Failed to replace {FilePath} after {MaxRetries} attempts.", FilePath, maxRetries);
				return false;
			}

			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Failed writing local storage payload to {FilePath}", FilePath);
			return false;
		}
		finally
		{
			if (File.Exists(tempFilePath))
			{
				try
				{
					File.Delete(tempFilePath);
				}
				catch (Exception cleanupEx)
				{
					_logger?.LogDebug(cleanupEx, "Failed to clean up temporary file {TempFilePath}", tempFilePath);
				}
			}

			_fileLock.Release();
		}
	}

	/// <inheritdoc />
	public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			if (!Directory.Exists(TargetDirectory))
			{
				Directory.CreateDirectory(TargetDirectory);
			}

			return Task.FromResult(true);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Connection test failed for local storage directory {TargetDirectory}", TargetDirectory);
			return Task.FromResult(false);
		}
	}
}
