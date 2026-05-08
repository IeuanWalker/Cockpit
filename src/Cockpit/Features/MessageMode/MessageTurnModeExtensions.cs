namespace Cockpit.Features.MessageMode;

public static class MessageTurnModeExtensions
{
	public const string ImmediateSdkToken = "immediate";
	public const string EnqueueSdkToken = "enqueue";

	public static string ToSdkToken(this MessageTurnModeEnum mode) => mode == MessageTurnModeEnum.Enqueue
		? EnqueueSdkToken
		: ImmediateSdkToken;
}
