using Cockpit.Models;
using Cockpit.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public sealed partial class WorkingPanel : IDisposable
{
	[Inject]
	ChatService ChatService { get; set; } = default!;

	[Parameter]
	public Models.ActivityGroup? Group { get; set; }

	[Parameter]
	public bool IsVisible { get; set; }

	[Parameter]
	public string MaxHeight { get; set; } = "35%";

	Timer? _timer;

	protected override void OnParametersSet()
	{
		// Start timer when thinking starts
		if(IsVisible && Group != null && Group.Status == GroupStatus.Running)
		{
			_timer ??= new Timer(_ => InvokeAsync(StateHasChanged), null, 0, 1000);
		}
		else
		{
			// Stop timer when thinking completes
			_timer?.Dispose();
			_timer = null;
		}
	}

	public void Dispose()
	{
		_timer?.Dispose();
		GC.SuppressFinalize(this);
	}

	string GetElapsedTime()
	{
		if(Group is null)
		{
			return string.Empty;
		}

		TimeSpan elapsed = Group.Status == GroupStatus.Running
			? DateTime.Now - Group.StartTime
			: (Group.EndTime ?? DateTime.Now) - Group.StartTime;

		return elapsed.Humanize();
	}

	string GenerateSummary()
	{
		if(Group is null)
		{
			return string.Empty;
		}

		List<ToolExecution> tools = [.. Group.Tools]; // Create snapshot to avoid collection modified exception
		int running = tools.Count(t => t.Status == ToolStatus.Running);
		int complete = tools.Count(t => t.Status == ToolStatus.Success);

		return $"{running} running, {complete} complete";
	}

	async Task StopSession()
	{
		await ChatService.AbortCurrentSessionAsync();
	}
}