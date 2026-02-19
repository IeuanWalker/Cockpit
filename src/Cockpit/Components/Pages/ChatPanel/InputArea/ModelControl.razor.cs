using Cockpit.Services;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ModelControl : ComponentBase, IDisposable
{
	[Inject] CopilotModelService _modelService { get; set; } = default!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;

	List<ModelInfo> _availableModels = [];
	bool _isModelDropdownOpen = false;
	bool _isReasoningEffortDropdownOpen = false;

	protected override async Task OnInitializedAsync()
	{
		_sessionManager.OnStateChanged += OnStateChanged;

		_availableModels = await _modelService.GetModels();
		if(_availableModels.Count > 0)
		{
			// TODO: Default model logic
			_sessionManager.CurrentSession?.Model = _availableModels[0];
			UpdateReasoningEffortForSelectedModel();
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void ToggleModelDropdown()
	{
		_isModelDropdownOpen = !_isModelDropdownOpen;
	}

	void SelectModel(ModelInfo model)
	{
		if(_sessionManager.CurrentSession is null)
		{
			return;
		}

		// Check if model actually changed
		if(_sessionManager.CurrentSession.Model.Id == model.Id)
		{
			_isModelDropdownOpen = false;
			return;
		}

		// Update model and mark session for restart
		_sessionManager.CurrentSession.Model = model;
		_sessionManager.CurrentSession.RequiresRestart = true;

		_isModelDropdownOpen = false;

		// Update reasoning effort based on new model's defaults
		UpdateReasoningEffortForSelectedModel();
	}

	void UpdateReasoningEffortForSelectedModel()
	{
		if(_sessionManager.CurrentSession?.Model is null)
		{
			return;
		}

		string newEffort = _sessionManager.CurrentSession.Model.DefaultReasoningEffort ?? string.Empty;

		// Only mark for restart if reasoning effort actually changed
		if(_sessionManager.CurrentSession.ReasoningEffort != newEffort)
		{
			_sessionManager.CurrentSession.ReasoningEffort = newEffort;
			_sessionManager.CurrentSession.RequiresRestart = true;
		}
	}

	void ToggleReasoningEffortDropdown()
	{
		_isReasoningEffortDropdownOpen = !_isReasoningEffortDropdownOpen;
	}

	void SelectReasoningEffort(string effort)
	{
		if(_sessionManager.CurrentSession is null)
		{
			return;
		}

		// Check if reasoning effort actually changed
		if(_sessionManager.CurrentSession.ReasoningEffort == effort)
		{
			_isReasoningEffortDropdownOpen = false;
			return;
		}

		// Update reasoning effort and mark session for restart
		_sessionManager.CurrentSession.ReasoningEffort = effort;
		_sessionManager.CurrentSession.RequiresRestart = true;

		_isReasoningEffortDropdownOpen = false;
	}

	string GetSelectedReasoningEffortDisplay()
	{
		if(string.IsNullOrEmpty(_sessionManager.CurrentSession?.ReasoningEffort))
		{
			return "Default";
		}

		return char.ToUpper(_sessionManager.CurrentSession.ReasoningEffort[0]) + _sessionManager.CurrentSession.ReasoningEffort[1..];
	}

	string GetDisplayModelName()
	{
		if(_sessionManager.CurrentSession is null)
		{
			return "No Model";
		}

		return _sessionManager.CurrentSession.Model.Name;
	}

	double GetDisplayModelMultiplier()
	{
		if(_sessionManager.CurrentSession is null)
		{
			return 1.0;
		}

		return _sessionManager.CurrentSession.Model.Billing?.Multiplier ?? 1.0;
	}

	string GetMultiplierColor(double multiplier)
	{
		if(_availableModels.Count == 0)
		{
			return "#999999";
		}

		double maxMultiplier = _availableModels.Max(m => m.Billing?.Multiplier ?? 0);

		if(multiplier == 0)
		{
			return "#00ff00";
		}
		else if(multiplier < 1)
		{
			return "#00d000";
		}
		else if(multiplier >= maxMultiplier)
		{
			return "#FF0000";
		}
		else if(multiplier > 1)
		{
			return "#ff8c00";
		}
		else
		{
			return "#999999";
		}
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
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}
