using Cockpit.Features.Auth;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class Main : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly AuthCheckFeature _authCheckFeature;

	public Main(SessionListFeature sessionListFeature, AuthCheckFeature authCheckFeature)
	{
		_sessionListFeature = sessionListFeature;
		_authCheckFeature = authCheckFeature;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_authCheckFeature.OnStateChanged += OnStateChanged;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await _authCheckFeature.CheckAuthAsync();
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
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
			_authCheckFeature.OnStateChanged -= OnStateChanged;
		}
	}
}
