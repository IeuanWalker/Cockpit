namespace Cockpit.Features.MessageMode;

/// <summary>
/// Controls how a user message interacts with any in-flight conversation turn.
/// </summary>
public enum MessageTurnModeEnum
{
	/// <summary>Send the message immediately, interrupting any running turn.</summary>
	Immediate,

	/// <summary>Queue the message behind any currently running turn.</summary>
	Enqueue
}
