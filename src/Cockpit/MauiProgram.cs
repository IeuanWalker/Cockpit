using Cockpit.Services;
using Cockpit.Services.Copilot;
using Cockpit.Services.Copilot.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		// Register application services
		builder.Services.AddScoped<LocalStorageService>();
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
