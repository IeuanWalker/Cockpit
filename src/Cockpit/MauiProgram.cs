using System.Text;
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
using Cockpit.Features.FileSearch;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Terminal;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.Theme;
using Cockpit.Features.Timestamp;
using Cockpit.Features.UIState;
using Cockpit.Features.Updates;
using Cockpit.Features.UserInputRequests;
using Cockpit.Utilities.Logging;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Media;
using MauiContentButton;
#if DEBUG && (WINDOWS || MACCATALYST)
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;
#endif
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Cockpit.Features.VSCode;

namespace Cockpit;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		RegisterCrashHandlers();

		MauiAppBuilder builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
#if WINDOWS || MACCATALYST
			.UseMauiCommunityToolkit()
#endif
			.AddMauiContentButtonHandler()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("SegoeUI-Regular.ttf", "SegoeUIRegular");
				fonts.AddFont("FluentSystemIcons-Regular.ttf", "FluentSystemIconsRegular");
			})
			.ConfigureEssentials(essentials =>
			{
				essentials.UseVersionTracking();
			});

		// Core Blazor and Toolkit services
		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSonner();
		builder.Services.RemoveAll<ToastService>();
		builder.Services.AddSingleton<ToastService>();

#if DEBUG && (WINDOWS || MACCATALYST)
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.AddMauiDevFlowAgent();
		builder.AddMauiBlazorDevFlowTools();
		builder.Logging.AddDebug();
#endif
		builder.Logging.AddProvider(new FileLoggerProvider());
		builder.Logging.AddFilter<FileLoggerProvider>(null, LogLevel.Information);

		// Speech and Text features
		builder.Services.AddSingleton<ISpeechToText, OfflineSpeechToTextImplementation>();
		builder.Services.AddSingleton(TextToSpeech.Default);
		builder.Services.AddSingleton<TextToSpeechFeature>();

		// UI and App features
		builder.Services.AddSingleton<IAppSettingsFeature, AppSettingsFeature>();
		builder.Services.AddSingleton<ThemeStateService>();
		builder.Services.AddScoped<ThemeFeature>();
		builder.Services.AddScoped<MarkdownFeature>();
		builder.Services.AddSingleton<UIStateFeature>();
		builder.Services.AddSingleton<TimestampFeature>();
		builder.Services.AddSingleton<TerminalFeature>();
		builder.Services.AddSingleton<VsCodeFeature>();
		builder.Services.AddSingleton<GitFeature>();
		builder.Services.AddSingleton<SplashFeature>();

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
		builder.Services.AddSingleton(sp =>
		{
			// HttpClient is created exclusively for UpdateFeature, which takes ownership and disposes it.
			HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
			client.DefaultRequestHeaders.Add("User-Agent", "Cockpit");
			return new UpdateFeature(client);
		});

		// File search
		builder.Services.AddSingleton<IFileSearchFeature, FileSearchFeature>();

		// Register agent services
		builder.Services.AddSingleton<AgentPersistence>();
		builder.Services.AddSingleton<GlobalAgentFeature>();
		builder.Services.AddSingleton<SessionAgentFeature>();

		MauiApp app = builder.Build();
		app.Services.GetRequiredService<UpdateFeature>().Initialize();
		return app;
	}

	static void RegisterCrashHandlers()
	{
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			LogCrash("AppDomain", args.ExceptionObject as Exception);

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			// Suppress the benign SDK exception when the CLI process exits and the
			// background monitor checks HasExited on an already-disposed Process object.
			bool isSdkProcessGone = args.Exception
				.Flatten().InnerExceptions
				.All(e => e is InvalidOperationException
					&& e.Message.Contains("No process is associated")
					&& (e.StackTrace?.Contains("GitHub.Copilot.SDK") ?? false));

			// Suppress the benign Blazor WebView exception when SendMessage is called
			// after the WebView2 control has been torn down (e.g. window close).
			bool isWebViewSendMessage = args.Exception
				.Flatten().InnerExceptions
				.All(e => e.StackTrace?.Contains("WebView2WebViewManager.SendMessage") ?? false);

			if(!isSdkProcessGone && !isWebViewSendMessage)
			{
				LogCrash("TaskScheduler", args.Exception);
			}

			args.SetObserved();
		};
	}

	static void LogCrash(string source, Exception? ex)
	{
		try
		{
			string logPath = Path.Combine(LogDirectoryHelper.LogDirectory, "crash.log");
			const long maxBytes = 5 * 1024 * 1024;

			FileInfo info = new(logPath);
			if(info.Exists && info.Length >= maxBytes)
			{
				string backup = logPath + ".old";
				if(File.Exists(backup))
				{
					File.Delete(backup);
				}

				File.Move(logPath, backup);
			}

			IEnumerable<Exception> exceptions = ex is AggregateException agg
				? agg.Flatten().InnerExceptions
				: (IEnumerable<Exception>)(ex is null ? [] : [ex]);

			StringBuilder sb = new();
			foreach(Exception inner in exceptions)
			{
				sb.AppendLine();
				sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] ===");
				sb.AppendLine(inner.ToString());
			}

			File.AppendAllText(logPath, sb.ToString());
		}
		catch { /* best-effort */ }
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
