using Cockpit.Features.AppSettings;
using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class EditedFilesRoot : ComponentBase, IAsyncDisposable
{
	readonly IJSRuntime _jsRuntime;
	readonly ThemeStateFeature _themeStateFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly EditedFilesWindowService _windowService;
	readonly EditedFilesSplashFeature _splashFeature;

	public EditedFilesRoot(
		IJSRuntime jsRuntime,
		ThemeStateFeature themeStateFeature,
		SessionListFeature sessionListFeature,
		IAppSettingsFeature appSettingsFeature,
		EditedFilesWindowService windowService,
		EditedFilesSplashFeature splashFeature)
	{
		_jsRuntime = jsRuntime;
		_themeStateFeature = themeStateFeature;
		_sessionListFeature = sessionListFeature;
		_appSettingsFeature = appSettingsFeature;
		_windowService = windowService;
		_splashFeature = splashFeature;
	}

	List<GitChangedFileModel> Files => _sessionListFeature.CurrentSession?.Context?.EditedFiles ?? [];

	GitChangedFileModel? _selectedFile;
	bool _splitView;

	string? _selectedFilePath =>
		_selectedFile is null ? null :
		_sessionListFeature.CurrentSession?.Context?.GitRoot is string root
			? Path.Combine(root, _selectedFile.Path.Replace('/', Path.DirectorySeparatorChar))
			: _selectedFile.Path;

	protected override void OnInitialized()
	{
		_splitView = _appSettingsFeature.DiffSplitView;
		_sessionListFeature.OnStateChanged += OnStateChanged;

		GitChangedFileModel? initial = _windowService.PendingInitialFile ?? Files.FirstOrDefault();
		_windowService.ConsumePendingInitialFile();
		if(initial is not null)
		{
			SelectFile(initial);
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await ApplyThemeAsync();
			_themeStateFeature.OnThemeChanged += OnThemeChangedHandler;
			_splashFeature.NotifyBlazorReady();
		}
	}

	void OnStateChanged()
	{
		if(_selectedFile is not null && !Files.Contains(_selectedFile))
		{
			GitChangedFileModel? refreshed = Files.FirstOrDefault(f => f.Path == _selectedFile.Path);
			_selectedFile = refreshed;
		}

		InvokeAsync(StateHasChanged);
	}

	void SelectFile(GitChangedFileModel file) => _selectedFile = file;

	void SetSplitView(bool split)
	{
		_splitView = split;
		_appSettingsFeature.DiffSplitView = split;
	}

	void RevealFile()
	{
		FileUtil.RevealFile(_selectedFilePath);
	}

	void OnThemeChangedHandler() => _ = ApplyThemeAsync();

	async Task ApplyThemeAsync()
	{
		try
		{
			if(_themeStateFeature.IsLightTheme)
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
			}

			await _jsRuntime.InvokeVoidAsync("cockpit.setAccentColor", _themeStateFeature.AccentColor, _themeStateFeature.AccentHoverColor);
		}
		catch { /* best-effort */ }
	}

	public async ValueTask DisposeAsync()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
		_themeStateFeature.OnThemeChanged -= OnThemeChangedHandler;
	}
}
