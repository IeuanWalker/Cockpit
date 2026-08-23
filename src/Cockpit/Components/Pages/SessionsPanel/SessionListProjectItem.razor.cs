using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class SessionListProjectItem
{
	[Inject] public required IJSRuntime JSRuntime { get; set; }

	[Parameter] public required string GroupId { get; set; }
	[Parameter] public required string Name { get; set; }
	[Parameter] public bool IsExpanded { get; set; }
	[Parameter] public bool IsPinned { get; set; }
	[Parameter] public string? CreateSessionPath { get; set; }
	[Parameter] public string? Repository { get; set; }
	[Parameter] public int SessionCount { get; set; }
	[Parameter] public EventCallback OnToggle { get; set; }
	[Parameter] public EventCallback OnCreate { get; set; }
	[Parameter] public EventCallback OnTogglePin { get; set; }

	ElementReference _projectItem;
	ElementReference _tooltip;
	readonly string _tooltipId = $"project-tooltip-{Guid.NewGuid():N}";

	string CreateSessionLocation => CreateSessionPath ?? "Default working directory";
	string PinActionLabel => IsPinned ? "Unpin project" : "Pin project";
	string SessionCountText => SessionCount == 1 ? "1 session" : $"{SessionCount} sessions";
	string TooltipId => _tooltipId;

	Task HandleToggle() => OnToggle.InvokeAsync();
	Task HandleCreate() => OnCreate.InvokeAsync();
	Task HandleTogglePin() => OnTogglePin.InvokeAsync();

	async Task ShowTooltip() => await JSRuntime.InvokeVoidAsync("cockpit.showSessionTooltip", _projectItem, _tooltip);
	async Task HideTooltip() => await JSRuntime.InvokeVoidAsync("cockpit.hideSessionTooltip", _tooltip);
}
