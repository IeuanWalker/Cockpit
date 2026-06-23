namespace Cockpit.Features.Sessions.Models;

public class TokenUsageInfoModel
{
	public double? ConversationTokens { get; set; }
	public double CurrentTokens { get; set; }
	public bool? IsInitial { get; set; }
	public double MessagesLength { get; set; }
	public double? SystemTokens { get; set; }
	public double TokenLimit { get; set; }
	public double? ToolDefinitionsTokens { get; set; }
}
