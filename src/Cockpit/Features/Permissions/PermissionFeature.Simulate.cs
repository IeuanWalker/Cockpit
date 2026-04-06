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
							Get-ChildItem "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\*.received.txt" | Sort-Object Name | ForEach-Object {
								$id = $_.Name -replace '.*id=(\d+)\.received\.txt', '$1'
								$content = Get-Content $_.FullName -Raw
								"Test $id : $content"
							} | Select-Object -Last 24
							Get-ChildItem "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\*.received.txt" | Sort-Object Name | ForEach-Object {
								$id = $_.Name -replace '.*id=(\d+)\.received\.txt', '$1'
								$content = Get-Content $_.FullName -Raw
								"Test $id : $content"
							} | Select-Object -Last 24
							Get-ChildItem "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\*.received.txt" | Sort-Object Name | ForEach-Object {
								$id = $_.Name -replace '.*id=(\d+)\.received\.txt', '$1'
								$content = Get-Content $_.FullName -Raw
								"Test $id : $content"
							} | Select-Object -Last 24
							""",
			Commands = ["rm", "npm"],
			Intention = "Clean and rebuild the project",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			IsDestructive = true,
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserApprovalAsync(request);
	}
}
