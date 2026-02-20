using Blazor.Sonner.Extensions;
using Blazor.Sonner.Services;
using Cockpit.Features.Connection;
using Cockpit.Features.Permissions;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.Terminal;
using Cockpit.Features.Theme;
using Cockpit.Services;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Media;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Cockpit;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSonner();
		// Re-register as singleton so it's accessible from singleton services in MAUI Blazor
		builder.Services.RemoveAll<ToastService>();
		builder.Services.AddSingleton<ToastService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<ISpeechToText, OfflineSpeechToTextImplementation>();

		// Register application services
		builder.Services.AddScoped<ThemeFeature>();
		builder.Services.AddScoped<MarkdownService>();
		builder.Services.AddSingleton<UIStateService>();
		builder.Services.AddSingleton<TimestampService>();
		builder.Services.AddSingleton<TerminalFeature>();

		// Register Copilot SDK services
		builder.Services.AddSingleton<CopilotClientService>();
		builder.Services.AddSingleton<ConnectionFeature>();
		builder.Services.AddSingleton<GlobalPermissionFeature>();
		builder.Services.AddSingleton<SessionPermissionFeature>();
		builder.Services.AddSingleton<SessionEventProcessor>();

		// Register UnifiedSessionManager first (no PermissionFeature dependency in constructor)
		builder.Services.AddSingleton<UnifiedSessionManager>();
		builder.Services.AddSingleton<ISessionStateProvider>(sp => sp.GetRequiredService<UnifiedSessionManager>());

		// Register PermissionFeature (depends on ISessionStateProvider)
		builder.Services.AddSingleton<PermissionFeature>(sp =>
		{
			PermissionFeature permissionFeature = new(
				sp.GetRequiredService<GlobalPermissionFeature>(),
				sp.GetRequiredService<SessionPermissionFeature>(),
				sp.GetRequiredService<ISessionStateProvider>(),
				sp.GetRequiredService<ILogger<PermissionFeature>>());

			// Wire up the circular reference
			sp.GetRequiredService<UnifiedSessionManager>().SetPermissionFeature(permissionFeature);

			return permissionFeature;
		});

		builder.Services.AddSingleton<CopilotModelService>();

		return builder.Build();
	}
}

#if NET10_0 && !MACCATALYST && !WINDOWS
/// <summary>
/// !Important: This is required to allow unit tests to work
/// </summary>
public class Program
{
	public static void Main(string[] args)
	{

	}
}
#endif
