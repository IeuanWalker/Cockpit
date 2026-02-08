using Microsoft.Extensions.Logging;
using CopilotGUI.Services;

namespace CopilotGUI;

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
		builder.Services.AddSingleton<ChatService>();
		builder.Services.AddSingleton<ContextService>();
		builder.Services.AddSingleton<TimestampService>();

		return builder.Build();
	}
}
