namespace Cockpit.Features.MessageMode;

/// <summary>
/// Conversion helpers between <see cref="MessageTurnModeEnum"/> and the SDK token strings
/// used when creating a Copilot session.
/// </summary>
public static class MessageTurnModeExtensions
{
	/// <summary>SDK token for <see cref="MessageTurnModeEnum.Immediate"/>.</summary>
	public const string ImmediateSdkToken = "immediate";

	/// <summary>SDK token for <see cref="MessageTurnModeEnum.Enqueue"/>.</summary>
	public const string EnqueueSdkToken = "enqueue";

	/// <summary>
	/// Returns the SDK token string for the given <paramref name="mode"/>.
	/// </summary>
	public static string ToSdkToken(this MessageTurnModeEnum mode) =>
		mode == MessageTurnModeEnum.Enqueue ? EnqueueSdkToken : ImmediateSdkToken;
}
