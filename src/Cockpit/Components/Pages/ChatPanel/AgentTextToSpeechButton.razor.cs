using Blazor.Sonner.Services;
using Cockpit.Features.TextToSpeech;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class AgentTextToSpeechButton : IDisposable
{
	[Parameter] public string MessageId { get; set; } = string.Empty;
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public bool Disabled { get; set; }

	readonly TextToSpeechFeature _textToSpeechFeature;
	readonly ToastService _toastService;
	public AgentTextToSpeechButton(TextToSpeechFeature textToSpeechFeature, ToastService toastService)
	{
		_textToSpeechFeature = textToSpeechFeature;
		_toastService = toastService;
	}

	protected override void OnInitialized()
	{
		_textToSpeechFeature.OnStateChanged += OnTtsStateChanged;
	}

	void OnTtsStateChanged()
	{
		_ = InvokeAsync(StateHasChanged);
	}

	async Task OnClick()
	{
		try
		{
			await _textToSpeechFeature.Speak(MessageId, Content);
		}
		catch(Exception ex)
		{
			_toastService.Error("Text-to-Speech Error", opts => opts.Description = ex.Message);
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
			_textToSpeechFeature.OnStateChanged -= OnTtsStateChanged;
		}
	}
}