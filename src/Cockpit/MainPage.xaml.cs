using Cockpit.Features.Agents;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Microsoft.JSInterop;

namespace Cockpit;

public partial class MainPage : ContentPage
{
	readonly GlobalAgentFeature _globalAgentFeature;
	readonly SplashFeature _splashService;
	readonly SessionFeature _sessionFeature;
	int _splashHidden;

#if WINDOWS
	Microsoft.UI.Xaml.Window? _winUIWindow;
	Microsoft.Web.WebView2.Core.CoreWebView2? _coreWebView2;
#endif

	public MainPage(GlobalAgentFeature globalAgentFeature, SplashFeature splashService, SessionFeature sessionFeature)
	{
		InitializeComponent();

		_globalAgentFeature = globalAgentFeature;
		_splashService = splashService;
		_sessionFeature = sessionFeature;

#if WINDOWS
		blazorWebView.BlazorWebViewInitialized += (s, e) =>
		{
			e.WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
			_coreWebView2 = e.WebView.CoreWebView2;
			_coreWebView2.ContextMenuRequested += OnWebViewContextMenuRequested;
		};
#endif

		_splashService.OnBlazorReady += OnBlazorReady;

		// Safety timeout - hide splash after 15 seconds
		Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
		{
			_ = HideSplash();
			return false;
		});
	}

	void OnBlazorReady()
	{
		Dispatcher.Dispatch(() => _ = HideSplash());
	}

	async Task HideSplash()
	{
		if(Interlocked.Exchange(ref _splashHidden, 1) != 0)
		{
			return;
		}

		await splashOverlay.FadeToAsync(0, 400, Easing.CubicOut);
		splashOverlay.IsVisible = false;
		// Remove from tree so it cannot intercept input (WinUI hidden views can block scroll)
		((Grid)Content).Children.Remove(splashOverlay);
		blazorWebView.Focus();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
#if WINDOWS
		if (Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
		{
			_winUIWindow = nativeWindow;
			nativeWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
		}
#endif
		// Fire and forget — starts loading sessions before Blazor is ready
		_ = _sessionFeature.LoadExistingSessions();
		await _globalAgentFeature.Load();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_splashService.OnBlazorReady -= OnBlazorReady;
#if WINDOWS
		if (_coreWebView2 is not null)
		{
			_coreWebView2.ContextMenuRequested -= OnWebViewContextMenuRequested;
		}
#endif
	}

	public async Task InvokeJavaScriptAsync(string script)
	{
		try
		{
			await blazorWebView.TryDispatchAsync(async (sp) =>
			{
				IJSRuntime jsRuntime = sp.GetRequiredService<IJSRuntime>();
				try
				{
					await jsRuntime.InvokeVoidAsync("eval", script);
				}
				catch
				{
					// Handle error silently
				}
			});
		}
		catch
		{
			// Handle error silently
		}
	}

#if WINDOWS
	void OnWebViewContextMenuRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs args)
	{
		if (!args.ContextMenuTarget.IsEditable)
		{
			args.Handled = true; // suppress context menu outside the editable input area
			return;
		}

		var deferral = args.GetDeferral();
		args.Handled = true;

		try
		{
			IReadOnlyList<string> spellSuggestions = [];
			if (args.MenuItems.Any(m => m.Name == "spellcheck"))
			{
				string word = args.ContextMenuTarget.SelectionText?.Trim() ?? "";
				if (!string.IsNullOrWhiteSpace(word))
					spellSuggestions = WindowsSpellChecker.GetSuggestions(word);
			}

			var flyout = new Microsoft.UI.Xaml.Controls.MenuFlyout();
			int selectedCmdId = -1;
			bool completed = false;

			void Finish()
			{
				if (completed) return;
				completed = true;
				if (selectedCmdId >= 0)
					args.SelectedCommandId = selectedCmdId;
				deferral.Complete();
			}

			Microsoft.UI.Xaml.FrameworkElement? root = _winUIWindow?.Content as Microsoft.UI.Xaml.FrameworkElement;
			if (root is null && Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w)
			{
				root = w.Content as Microsoft.UI.Xaml.FrameworkElement;
			}

			Microsoft.UI.Xaml.ElementTheme theme = root?.ActualTheme ?? Microsoft.UI.Xaml.ElementTheme.Dark;
			BuildFlyoutItems(flyout.Items, args.MenuItems, id => selectedCmdId = id, theme, spellSuggestions);
			flyout.Closed += (_, _) => Finish();

			if (root is not null)
			{
				flyout.ShowAt(root, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
				{
					Position = new Windows.Foundation.Point(args.Location.X, args.Location.Y),
					Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
				});
			}
			else
			{
				deferral.Complete();
			}
		}
		catch
		{
			deferral.Complete();
		}
	}

	static void BuildFlyoutItems(
		IList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase> items,
		IList<Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItem> menuItems,
		Action<int> onSelect,
		Microsoft.UI.Xaml.ElementTheme theme,
		IReadOnlyList<string> spellSuggestions)
	{
		var foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
			theme == Microsoft.UI.Xaml.ElementTheme.Dark
				? Windows.UI.Color.FromArgb(255, 204, 204, 204)
				: Windows.UI.Color.FromArgb(255, 59, 59, 59));

		int suggestionIndex = 0;
		foreach (Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItem menuItem in menuItems)
		{
			if (menuItem.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Separator)
			{
				items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
			}
			else if (menuItem.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Submenu)
			{
				var sub = new Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem
				{
					Text = menuItem.Label.Replace("&", ""),
					Foreground = foreground,
				};
				BuildFlyoutItems(sub.Items, menuItem.Children, onSelect, theme, spellSuggestions);
				items.Add(sub);
			}
			else
			{
				int cmdId = menuItem.CommandId;
				string label;
				if (menuItem.Name == "spellcheck" && suggestionIndex < spellSuggestions.Count)
				{
					label = spellSuggestions[suggestionIndex++];
				}
				else
				{
					label = menuItem.Label.Replace("&", "");
				}

				var item = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
				{
					Text = label,
					Foreground = foreground,
				};
				item.Click += (_, _) => onSelect(cmdId);
				items.Add(item);
			}
		}
	}
#endif
}