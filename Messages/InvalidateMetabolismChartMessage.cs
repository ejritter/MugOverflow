using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MugOverflowProject.Messages;

public sealed class InvalidateMetabolismChartMessage : ValueChangedMessage<bool>
{
	public InvalidateMetabolismChartMessage() : base(true) { }
	public InvalidateMetabolismChartMessage(bool value) : base(value) { }
}
