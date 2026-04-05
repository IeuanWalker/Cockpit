#if WINDOWS
using Cockpit.Platforms.Windows;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Cockpit;

public partial class MainPage
{
	Microsoft.UI.Xaml.Window? _winUIWindow;
	CoreWebView2? _coreWebView2;

	void ConfigureWindowsContextMenu()
	{
		blazorWebView.BlazorWebViewInitialized += (s, e) =>
		{
			e.WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
			_coreWebView2 = e.WebView.CoreWebView2;
			_coreWebView2.ContextMenuRequested += OnWebViewContextMenuRequested;
		};
	}

	void ConfigureWindowsContextMenuOnAppearing()
	{
		if(Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
		{
			_winUIWindow = nativeWindow;
			nativeWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
		}
	}

	void TeardownWindowsContextMenu()
	{
		_coreWebView2?.ContextMenuRequested -= OnWebViewContextMenuRequested;
	}

	void OnWebViewContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs args)
	{
		if(!args.ContextMenuTarget.IsEditable)
		{
			args.Handled = true; // suppress context menu outside the editable input area
			return;
		}

		Deferral deferral = args.GetDeferral();
		args.Handled = true;

		try
		{
			IReadOnlyList<string> spellSuggestions = [];
			if(args.MenuItems.Any(m => m.Name == "spellcheck"))
			{
				string word = args.ContextMenuTarget.SelectionText?.Trim() ?? "";
				if(!string.IsNullOrWhiteSpace(word))
				{
					spellSuggestions = WindowsSpellChecker.GetSuggestions(word);
				}
			}

			Microsoft.UI.Xaml.Controls.MenuFlyout flyout = new();
			int selectedCmdId = -1;
			bool completed = false;

			void Finish()
			{
				if(completed)
				{
					return;
				}

				completed = true;
				if(selectedCmdId >= 0)
				{
					args.SelectedCommandId = selectedCmdId;
				}

				deferral.Complete();
			}

			Microsoft.UI.Xaml.FrameworkElement? root = _winUIWindow?.Content as Microsoft.UI.Xaml.FrameworkElement;
			if(root is null && Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w)
			{
				root = w.Content as Microsoft.UI.Xaml.FrameworkElement;
			}

			Microsoft.UI.Xaml.ElementTheme theme = root?.ActualTheme ?? Microsoft.UI.Xaml.ElementTheme.Dark;
			BuildFlyoutItems(flyout.Items, args.MenuItems, id => selectedCmdId = id, theme, spellSuggestions);
			flyout.Closed += (_, _) => Finish();

			if(root is not null)
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

	static readonly HashSet<string> allowedMenuItemNames =
	[
		"spellcheck",
		"cut",
		"copy",
		"paste",
		"emoji",
		"insertEmoji",
		"selectAll",
	];

	static void BuildFlyoutItems(
		IList<Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase> items,
		IList<CoreWebView2ContextMenuItem> menuItems,
		Action<int> onSelect,
		Microsoft.UI.Xaml.ElementTheme theme,
		IReadOnlyList<string> spellSuggestions)
	{
		Microsoft.UI.Xaml.Media.SolidColorBrush foreground = new(
			theme == Microsoft.UI.Xaml.ElementTheme.Dark
				? Windows.UI.Color.FromArgb(255, 204, 204, 204)
				: Windows.UI.Color.FromArgb(255, 59, 59, 59));

		int suggestionIndex = 0;
		foreach(CoreWebView2ContextMenuItem menuItem in menuItems)
		{
			if(menuItem.Kind == CoreWebView2ContextMenuItemKind.Separator)
			{
				// Add separator only if there's a preceding non-separator item
				if(items.Count > 0 && items[^1] is not Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator)
				{
					items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
				}
			}
			else if(menuItem.Kind == CoreWebView2ContextMenuItemKind.Submenu)
			{
				if(!allowedMenuItemNames.Contains(menuItem.Name))
				{
					continue;
				}

				Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem sub = new()
				{
					Text = menuItem.Label.Replace("&", ""),
					Foreground = foreground,
				};
				BuildFlyoutItems(sub.Items, menuItem.Children, onSelect, theme, spellSuggestions);
				items.Add(sub);
			}
			else
			{
				if(!allowedMenuItemNames.Contains(menuItem.Name))
				{
					continue;
				}

				int cmdId = menuItem.CommandId;
				string label;
				if(menuItem.Name == "spellcheck")
				{
					if(suggestionIndex >= spellSuggestions.Count)
					{
						continue; // skip empty placeholder slots
					}

					label = spellSuggestions[suggestionIndex++];
				}
				else
				{
					label = menuItem.Label.Replace("&", "");
				}

				Microsoft.UI.Xaml.Controls.MenuFlyoutItem item = new()
				{
					Text = label,
					Foreground = foreground,
				};
				item.Click += (_, _) => onSelect(cmdId);
				items.Add(item);
			}
		}

		// Remove trailing separator if present
		if(items.Count > 0 && items[^1] is Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator)
		{
			items.RemoveAt(items.Count - 1);
		}
	}
}
#endif
