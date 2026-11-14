using System.Runtime.InteropServices;
using MugOverflowProject.Services.Diagnostics;
using Xunit;

namespace MugOverflowProject.Tests.Diagnostics;

public sealed class WinUiCrashPolicyTests
{
	[Fact]
	public void IsUnrecoverableException_NullInputs_ReturnsFalse()
	{
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(null, null));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(null, string.Empty));
	}

	[Theory]
	[InlineData("Window was already activated")]
	[InlineData("Window was already deactivated")]
	[InlineData("Operation failed due to network timeout")]
	[InlineData("An unexpected database error occurred")]
	public void IsUnrecoverableException_RecoverableLifecycleExceptions_ReturnsFalse(string message)
	{
		var ex = new InvalidOperationException(message);
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(ex, ex.Message));
	}

	[Fact]
	public void IsUnrecoverableException_COMException_WrongThreadHResult_ReturnsTrue()
	{
		const int RPC_E_WRONG_THREAD = unchecked((int)0x8001010E);
		var ex = new COMException("The application called an interface that was marshalled for a different thread.", RPC_E_WRONG_THREAD);

		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(ex));
	}

	[Fact]
	public void IsUnrecoverableException_MessageContainsHexCode_ReturnsTrue()
	{
		var ex = new Exception("Fatal failure code 0x8001010E encountered during WinUI layout pass.");
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(ex));
	}

	[Fact]
	public void IsUnrecoverableException_MessageContainsMarshalledForDifferentThread_ReturnsTrue()
	{
		var ex = new Exception("The application called an interface that was marshalled for a different thread.");
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(ex));
	}

	[Fact]
	public void IsUnrecoverableException_NullException_EventMessageContainsFatalSignatures_ReturnsTrue()
	{
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(null, "Native crash: 0x8001010E"));
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(null, "RPC_E_WRONG_THREAD on native UI thread"));
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(null, "marshalled for a different thread"));
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(null, "Compositor failure occurred"));
	}

	[Fact]
	public void IsUnrecoverableException_CompositorOrDispatcherQueueShutdown_ReturnsTrue()
	{
		var compEx = new Exception("Compositor encountered fatal direct composition device lost error.");
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(compEx));

		var dqEx = new Exception("DispatcherQueue is already in shutdown state.");
		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(dqEx));
	}

	[Fact]
	public void IsUnrecoverableException_NestedAggregateException_DetectsInnerFatalCOMError()
	{
		const int RPC_E_WRONG_THREAD = unchecked((int)0x8001010E);
		var innerCom = new COMException("Wrong thread", RPC_E_WRONG_THREAD);
		var nestedAgg = new AggregateException(new InvalidOperationException("Outer wrapper"), innerCom);
		var topAgg = new AggregateException(new Exception("Top level"), nestedAgg);

		Assert.True(WinUiCrashPolicy.IsUnrecoverableException(topAgg));
	}

	[Fact]
	public void IsUnrecoverableException_StandardExceptions_ReturnsFalse()
	{
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new ArgumentNullException("arg")));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new TimeoutException("timed out")));
		Assert.False(WinUiCrashPolicy.IsUnrecoverableException(new HttpRequestException("network down")));
	}
}
