namespace Cockpit.Features.Connection;

public class ConnectionCheckRecordModel
{
	public required ConnectionStatusEnum Status { get; set; }
	public required DateTime CheckedAt { get; set; }
	public required string ResponseJson { get; set; }
}