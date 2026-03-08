using Blazor.Sonner.Extensions;
using Blazor.Sonner.Services;
using Cockpit.Features.Agents;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Connection;
using Cockpit.Features.Git;
using Cockpit.Features.Markdown;
using Cockpit.Features.Models;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sdk;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.Sessions;
using Cockpit.Features.Terminal;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.Theme;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Cockpit.Features.Updates;
using Cockpit.Features.UserInputRequests;
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
#if WINDOWS || MACCATALYST
			.UseMauiCommunityToolkit()
#endif
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("FluentSystemIcons-Light.ttf", "FluentSystemIconsLight");
			});

		// Core Blazor and Toolkit services
		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSonner();
		builder.Services.RemoveAll<ToastService>();
		builder.Services.AddSingleton<ToastService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		// Speech and Text features
		builder.Services.AddSingleton<ISpeechToText, OfflineSpeechToTextImplementation>();
		builder.Services.AddSingleton<ITextToSpeech>(TextToSpeech.Default);
		builder.Services.AddSingleton<TextToSpeechFeature>();

		// UI and App features
		builder.Services.AddSingleton<IAppSettingsFeature, AppSettingsFeature>();
		builder.Services.AddScoped<ThemeFeature>();
		builder.Services.AddScoped<MarkdownFeature>();
		builder.Services.AddSingleton<UIStateFeature>();
		builder.Services.AddSingleton<TimestampFeature>();
		builder.Services.AddSingleton<TerminalFeature>();
		builder.Services.AddSingleton<GitFeature>();

		// Copilot SDK and Permissions
		builder.Services.AddSingleton<CopilotClientFeature>();
		builder.Services.AddSingleton<ConnectionFeature>();
		builder.Services.AddSingleton<GlobalPermissionFeature>();
		builder.Services.AddSingleton<GlobalDenyFeature>();
		builder.Services.AddSingleton<SessionPermissionFeature>();
		builder.Services.AddSingleton<SessionEventProcessor>();

		// Session management
		builder.Services.AddSingleton<SdkSessionRegistry>();
		builder.Services.AddSingleton<SessionListFeature>();
		builder.Services.AddSingleton<ISessionStateProvider>(sp => sp.GetRequiredService<SessionListFeature>());
		builder.Services.AddSingleton<SessionFeature>();

		// Permission features

		builder.Services.AddSingleton<PermissionFeature>();
		builder.Services.AddSingleton<IPermissionHandler>(sp => sp.GetRequiredService<PermissionFeature>());

		// Register UserInputFeature
		builder.Services.AddSingleton<UserInputFeature>();
		builder.Services.AddSingleton<IUserInputHandler>(sp => sp.GetRequiredService<UserInputFeature>());

		builder.Services.AddSingleton<ModelFeature>();
		builder.Services.AddSingleton<UpdateFeature>();

		// Register agent services
		builder.Services.AddSingleton<AgentPersistence>();
		builder.Services.AddSingleton<GlobalAgentFeature>();
		builder.Services.AddSingleton<SessionAgentFeature>();

		return builder.Build();
	}
}

#if NET10_0 && !MACCATALYST && !WINDOWS
/// <summary>
/// !Important: This is required to allow unit tests to work
/// </summary>
public class Program
{
	public static void Main(string[] _)
	{

	}
}
#endif
