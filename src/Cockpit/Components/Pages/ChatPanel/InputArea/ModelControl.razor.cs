using Cockpit.Features.Models;
using Cockpit.Features.Sessions;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class ModelControl : ComponentBase, IDisposable
{
	readonly ModelFeature _modelFeature;
	readonly SessionListFeature _sessionListFeature;
	public ModelControl(ModelFeature modelFeature, SessionListFeature sessionListFeature)
	{
		_modelFeature = modelFeature;
		_sessionListFeature = sessionListFeature;
	}

	IList<ModelInfo> _availableModels = [];
	PickerControl _modelPicker = default!;
	PickerControl? _reasoningPicker;

	protected override async Task OnInitializedAsync()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;

		_availableModels = await _modelFeature.GetModels();
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void SelectModel(ModelInfo model)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			return;
		}

		// Check if model actually changed
		if(_sessionListFeature.CurrentSession.Model.Id == model.Id)
		{
			_modelPicker.Close();
			return;
		}

		// Update model and mark session for restart
		_sessionListFeature.CurrentSession.Model = model;
		_sessionListFeature.CurrentSession.ModelChanged = true;

		_modelPicker.Close();

		// Update reasoning effort based on new model's defaults
		UpdateReasoningEffortForSelectedModel();

		// Persist model selection immediately (best-effort, fire-and-forget)
		_ = _modelFeature.SaveSessionModel(_sessionListFeature.CurrentSession);
	}

	void UpdateReasoningEffortForSelectedModel()
	{
		if(_sessionListFeature.CurrentSession?.Model is null)
		{
			return;
		}

		string newEffort = _sessionListFeature.CurrentSession.Model.DefaultReasoningEffort ?? string.Empty;
		string currentEffort = _sessionListFeature.CurrentSession.ReasoningEffort ?? string.Empty;

		// Only mark for restart if reasoning effort actually changed
		if(currentEffort != newEffort)
		{
			_sessionListFeature.CurrentSession.ReasoningEffort = newEffort;
			_sessionListFeature.CurrentSession.ModelChanged = true;
		}
	}

	void SelectReasoningEffort(string effort)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			return;
		}

		// Check if reasoning effort actually changed
		if(_sessionListFeature.CurrentSession.ReasoningEffort == effort)
		{
			_reasoningPicker?.Close();
			return;
		}

		// Update reasoning effort and mark session for restart
		_sessionListFeature.CurrentSession.ReasoningEffort = effort;
		_sessionListFeature.CurrentSession.ModelChanged = true;

		_reasoningPicker?.Close();

		// Persist model selection immediately (best-effort, fire-and-forget)
		_ = _modelFeature.SaveSessionModel(_sessionListFeature.CurrentSession);
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
			return "Unkown";
		}

		if(model.Id.Equals("Auto", StringComparison.InvariantCultureIgnoreCase))
		{
			return string.Empty;
		}

		if(model.Billing is null)
		{
			return "Unknown";
		}

		return $"{model.Billing.Multiplier:0.0}x";
	}

	string GetMultiplierColor(ModelInfo? model)
	{
		if(_availableModels.Count == 0 || model?.Billing?.Multiplier is null)
		{
			return "#999999";
		}

		double maxMultiplier = _availableModels.Max(m => m.Billing?.Multiplier ?? 0);

		if(model.Billing.Multiplier == 0)
		{
			return "#00ff00";
		}
		else if(model.Billing.Multiplier < 1)
		{
			return "#00d000";
		}
		else if(model.Billing.Multiplier == 1)
		{
			return "#999999";
		}
		else if(model.Billing.Multiplier >= maxMultiplier)
		{
			return "#FF0000";
		}
		else if(model.Billing.Multiplier > 1)
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
			_sessionListFeature.OnStateChanged -= OnStateChanged;
		}
	}
}
