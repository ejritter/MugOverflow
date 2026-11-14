using System.Runtime.InteropServices;

namespace MugOverflowProject.Services.Diagnostics;

/// <summary>
/// Diagnostic crash policy for isolating fatal WinUI COM wrong-thread marshaling (0x8001010E / RPC_E_WRONG_THREAD),
/// compositor crashes, and dispatcher queue shutdowns to prevent WinUI from swallowing fatal exceptions into zombie hangs.
/// </summary>
public static class WinUiCrashPolicy
{
	public const int RPC_E_WRONG_THREAD = unchecked((int)0x8001010E);

	/// <summary>
	/// Determines whether the exception or message represents an unrecoverable COM thread marshaling violation,
	/// compositor crash, or dispatcher queue failure that must terminate the process rather than being swallowed as handled.
	/// </summary>
	public static bool IsUnrecoverableException(Exception? ex, string? eventMessage = null)
	{
		if (!string.IsNullOrEmpty(eventMessage) && (
			eventMessage.Contains("0x8001010E", StringComparison.OrdinalIgnoreCase) ||
			eventMessage.Contains("RPC_E_WRONG_THREAD", StringComparison.OrdinalIgnoreCase) ||
			eventMessage.Contains("marshalled for a different thread", StringComparison.OrdinalIgnoreCase) ||
			eventMessage.Contains("Compositor", StringComparison.OrdinalIgnoreCase) ||
			eventMessage.Contains("Microsoft.UI.Composition", StringComparison.OrdinalIgnoreCase) ||
			(eventMessage.Contains("DispatcherQueue", StringComparison.OrdinalIgnoreCase) && eventMessage.Contains("shutdown", StringComparison.OrdinalIgnoreCase))))
		{
			return true;
		}

		if (ex == null) return false;

		if (ex is AggregateException aggEx)
		{
			foreach (var inner in aggEx.Flatten().InnerExceptions)
			{
				if (IsUnrecoverableException(inner))
					return true;
			}
		}

		for (var current = ex; current != null; current = current.InnerException)
		{
			if (current.HResult == RPC_E_WRONG_THREAD || (uint)current.HResult == 0x8001010E)
			{
				return true;
			}

			if (current is COMException comEx && (comEx.HResult == RPC_E_WRONG_THREAD || (uint)comEx.HResult == 0x8001010E))
			{
				return true;
			}

			var msg = current.Message;
			if (!string.IsNullOrEmpty(msg) && (
				msg.Contains("0x8001010E", StringComparison.OrdinalIgnoreCase) ||
				msg.Contains("RPC_E_WRONG_THREAD", StringComparison.OrdinalIgnoreCase) ||
				msg.Contains("marshalled for a different thread", StringComparison.OrdinalIgnoreCase) ||
				msg.Contains("Compositor", StringComparison.OrdinalIgnoreCase) ||
				msg.Contains("Microsoft.UI.Composition", StringComparison.OrdinalIgnoreCase) ||
				(msg.Contains("DispatcherQueue", StringComparison.OrdinalIgnoreCase) && msg.Contains("shutdown", StringComparison.OrdinalIgnoreCase))))
			{
				return true;
			}

			var stackTrace = current.StackTrace;
			if (!string.IsNullOrEmpty(stackTrace) && (
				stackTrace.Contains("Microsoft.UI.Composition", StringComparison.OrdinalIgnoreCase) ||
				stackTrace.Contains("Compositor", StringComparison.OrdinalIgnoreCase) ||
				(stackTrace.Contains("DispatcherQueue", StringComparison.OrdinalIgnoreCase) && stackTrace.Contains("shutdown", StringComparison.OrdinalIgnoreCase))))
			{
				return true;
			}

			var source = current.Source;
			if (!string.IsNullOrEmpty(source) && (
				source.Contains("Microsoft.UI.Composition", StringComparison.OrdinalIgnoreCase) ||
				source.Contains("WinUI.Composition", StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}

			var typeName = current.GetType().FullName ?? string.Empty;
			if (typeName.Contains("Compositor", StringComparison.OrdinalIgnoreCase) ||
				(typeName.Contains("DispatcherQueue", StringComparison.OrdinalIgnoreCase) && msg != null && msg.Contains("shutdown", StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}

		return false;
	}
}
