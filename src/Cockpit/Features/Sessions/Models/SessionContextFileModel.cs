namespace Cockpit.Features.Sessions.Models;

public class SessionContextFileModel
{
	public string Name { get; set; } = string.Empty;
	public string Path { get; set; } = string.Empty;
	public FileStatusEnum Status { get; set; } = FileStatusEnum.Unmodified;
}

public enum FileStatusEnum
{
	Unmodified,
	Modified,
	Added,
	Deleted
}
