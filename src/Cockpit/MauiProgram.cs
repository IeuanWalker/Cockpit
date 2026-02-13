using Blazor.Sonner.Extensions;
using Blazor.Sonner.Services;
using Cockpit.Services;
using Cockpit.Services.Copilot;
using Cockpit.Services.Copilot.Models;
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
		builder.Services.AddScoped<ThemeService>();
		builder.Services.AddScoped<MarkdownService>();
		builder.Services.AddSingleton<UIStateService>();
		builder.Services.AddSingleton<TimestampService>();
		builder.Services.AddSingleton<ContextService>();

		// Register Copilot SDK services
		builder.Services.AddSingleton<CopilotClientService>();
		builder.Services.AddSingleton<CopilotSessionManager>();
		builder.Services.AddSingleton<ChatService>();
		builder.Services.AddSingleton<ICopilotModelService, CopilotModelService>();

		return builder.Build();
	}
}
