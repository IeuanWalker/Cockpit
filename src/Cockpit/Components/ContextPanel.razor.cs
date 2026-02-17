using Cockpit.Models;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public partial class ContextPanel : ComponentBase, IDisposable
{
	DotNetObjectReference<ContextPanel>? _dotNetHelper;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] UnifiedSessionManager SessionManager { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	SessionContext? CurrentContext => SessionManager.CurrentSession?.Context;
	List<string> Branches => CurrentContext?.Branches ?? [];
	List<ContextFile> EditedFiles => CurrentContext?.EditedFiles ?? [];
	string CurrentDirectory => CurrentContext?.CurrentDirectory ?? string.Empty;
	string CurrentBranch => CurrentContext?.CurrentBranch ?? string.Empty;
	string McpServerUrl => CurrentContext?.McpServerUrl ?? string.Empty;
	bool McpServerConnected => CurrentContext?.McpServerConnected ?? false;

	void SetBranch(string branch) => SessionManager.SetCurrentSessionContextBranch(branch);
	void ToggleSkill(string skill) => SessionManager.ToggleCurrentSessionContextSkill(skill);
	bool IsSkillEnabled(string skill) => CurrentContext?.AgentSkills.Contains(skill) == true;

	protected override void OnInitialized()
	{
		UIState.OnStateChanged += OnStateChanged;
		SessionManager.OnStateChanged += OnStateChanged;

		// Initialize files dropdown as open
		UIState.SetDropdownOpen("files", true);
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetHelper = DotNetObjectReference.Create(this);
			await JSRuntime.InvokeVoidAsync("cockpit.initializeResize", "rightResizeHandle", "rightSidebar", "right", _dotNetHelper);
		}
	}

	[JSInvokable]
	public void OnResize(int width)
	{
		UIState.SetRightSidebarWidth(width);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			UIState.OnStateChanged -= OnStateChanged;
			SessionManager.OnStateChanged -= OnStateChanged;
			_dotNetHelper?.Dispose();
		}
	}
}
