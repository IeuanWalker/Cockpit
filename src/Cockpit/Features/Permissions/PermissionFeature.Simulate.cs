using Cockpit.Features.Permissions.Models;

namespace Cockpit.Features.Permissions;

public partial class PermissionFeature
{

	/// <summary>
	/// Simulate a permission request for debugging/testing.
	/// </summary>
	public Task SimulateRequest1(string sessionId)
	{
		PermissionRequestModel request = new()
		{
			SessionId = sessionId,
			RequestTitle = "Allow run terminal command",
			FullCommand = "rm -rf ./dist && npm run build",
			Commands = ["rm", "npm"],
			Intention = "Clean and rebuild the project",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			IsDestructive = true,
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserApprovalAsync(request);
	}

	/// <summary>
	/// Simulate a permission request for debugging/testing.
	/// </summary>
	public Task SimulateRequest2(string sessionId)
	{
		PermissionRequestModel request = new()
		{
			SessionId = sessionId,
			RequestTitle = "Allow run terminal command",
			FullCommand = """
							Get-ChildItem ".\tests\*.txt" | Sort-Object Name | ForEach-Object {
								$content = Get-Content $_.FullName -Raw
								"File: $($_.Name) — $content"
							} | Select-Object -Last 10
							""",
			Commands = ["Get-ChildItem", "Get-Content"],
			Intention = "List and display test output files",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			IsDestructive = false,
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserApprovalAsync(request);
	}
}
