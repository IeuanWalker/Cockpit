using Cockpit.Features.Byok;
using Cockpit.Features.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class ModelControl : ComponentBase, IDisposable
{
	readonly IModelFeature _modelFeature;
	readonly IByokFeature _byokFeature;
	readonly SessionListFeature _sessionListFeature;
	public ModelControl(IModelFeature modelFeature, IByokFeature byokFeature, SessionListFeature sessionListFeature)
	{
		_modelFeature = modelFeature;
		_byokFeature = byokFeature;
		_sessionListFeature = sessionListFeature;
	}

	IReadOnlyList<ModelInfo> _availableModels = [];
	double _maxMultiplier;
	PickerControl _modelPicker = default!;
	PickerControl? _reasoningPicker;
	Cockpit.Components.Popups.ModelInfoPopup _modelInfoPopup = default!;

	protected override async Task OnInitializedAsync()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_byokFeature.OnChanged += OnByokChanged;

		await RefreshModelsAsync();
	}

	void OnStateChanged()
	{
		_ = InvokeAsync(StateHasChanged);
	}

	void OnByokChanged()
	{
		_ = InvokeAsync(async () =>
		{
			await RefreshModelsAsync();
			StateHasChanged();
		});
	}

	async Task RefreshModelsAsync()
	{
		_availableModels = await _modelFeature.GetModels();
		_maxMultiplier = _availableModels.Count > 0
			? _availableModels.Max(m => m.Billing?.Multiplier ?? 0)
			: 0;
	}

	void OpenModelInfoPopup()
	{
		_modelInfoPopup.Open();
	}

	async Task HandleModelSelectedFromPopup(ModelInfo model)
	{
		await SelectModel(model);
	}

	async Task SelectModel(ModelInfo model)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			return;
		}

		if(_sessionListFeature.CurrentSession.Model.Id == model.Id)
		{
			_modelPicker.Close();
			return;
		}

		_sessionListFeature.CurrentSession.Model = model;
		_sessionListFeature.CurrentSession.ModelChanged = true;

		// Track BYOK config ID so the session layer knows to restart (not SetModelAsync) when switching providers
		ByokModelConfig? byokConfig = _byokFeature.GetAll().FirstOrDefault(c => c.ModelId == model.Id);
		if(byokConfig is not null)
		{
			_sessionListFeature.CurrentSession.ByokConfigId = byokConfig.Id;
		}

		_modelPicker.Close();

		UpdateReasoningEffortForSelectedModel();

		await _modelFeature.SaveSessionModel(_sessionListFeature.CurrentSession);
	}

	void UpdateReasoningEffortForSelectedModel()
	{
		if(_sessionListFeature.CurrentSession?.Model is null)
		{
			return;
		}

		string? newEffort = _sessionListFeature.CurrentSession.Model.DefaultReasoningEffort;

		if(_sessionListFeature.CurrentSession.ReasoningEffort != newEffort)
		{
			_sessionListFeature.CurrentSession.ReasoningEffort = newEffort;
			_sessionListFeature.CurrentSession.ModelChanged = true;
		}
	}

	async Task SelectReasoningEffort(string effort)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			return;
		}

		// Ignore selections that are not supported by the current model.
		if(_sessionListFeature.CurrentSession.Model.SupportedReasoningEfforts?.Contains(effort) != true)
		{
			return;
		}

		if(_sessionListFeature.CurrentSession.ReasoningEffort == effort)
		{
			_reasoningPicker?.Close();
			return;
		}

		_sessionListFeature.CurrentSession.ReasoningEffort = effort;
		_sessionListFeature.CurrentSession.ModelChanged = true;

		_reasoningPicker?.Close();

		await _modelFeature.SaveSessionModel(_sessionListFeature.CurrentSession);
	}

	string? GetModelPickerTooltip()
	{
		if(_sessionListFeature.CurrentSession?.Status == SessionStatusEnum.Running)
		{
			return "You can't change model while the agent is working";
		}

		if(_sessionListFeature.CurrentSession?.PendingPermissionRequests?.Count > 0)
		{
			return "You can't change model while a permission request is pending";
		}

		return null;
	}

	string GetSelectedReasoningEffortDisplay()
	{
		if(string.IsNullOrEmpty(_sessionListFeature.CurrentSession?.ReasoningEffort))
		{
			return "Default";
		}

		return char.ToUpper(_sessionListFeature.CurrentSession.ReasoningEffort[0]) + _sessionListFeature.CurrentSession.ReasoningEffort[1..];
	}

	string GetDisplayModelMultiplier(ModelInfo? model)
	{
		if(model is null)
		{
			return "Unknown";
		}

		if(model.Id.Equals("Auto", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		if(model.Billing is null)
		{
			return string.Empty;
		}

		return $"{model.Billing.Multiplier:0.0}x";
	}

	string GetMultiplierColor(ModelInfo? model)
	{
		if(_availableModels.Count == 0 || model?.Billing?.Multiplier is null)
		{
			return "#999999";
		}

		double multiplier = model.Billing.Multiplier;

		if(multiplier == 0)
		{
			return "#00ff00";
		}

		if(multiplier < 1)
		{
			return "#00d000";
		}

		if(multiplier == 1)
		{
			return "#999999";
		}

		if(multiplier >= _maxMultiplier)
		{
			return "#FF0000";
		}

		// multiplier > 1 and below the maximum
		return "#ff8c00";
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
			_sessionListFeature.OnStateChanged -= OnStateChanged;
			_byokFeature.OnChanged -= OnByokChanged;
		}
	}
}
