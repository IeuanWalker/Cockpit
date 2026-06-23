using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Auth;

/// <summary>
/// Handles the GitHub Copilot CLI login flow (OAuth device flow).
/// Supports both github.com and GitHub Enterprise hosts.
/// </summary>
public partial class AuthFeature
{
	readonly ILogger<AuthFeature> _logger;

	public record DeviceFlowInfo(string Url, string Code);

	public AuthFeature(ILogger<AuthFeature> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Spawns <c>copilot login</c> and monitors stdout/stderr for the device flow URL and code.
	/// Pass <paramref name="host"/> for enterprise accounts (e.g. <c>https://example.ghe.com</c>),
	/// or <c>null</c> for github.com.
	/// </summary>
	public async Task<bool> RunLoginAsync(
		string? host = null,
		Action<DeviceFlowInfo>? onDeviceFlow = null,
		CancellationToken cancellationToken = default)
	{
		List<string> args = ["login"];
		if(!string.IsNullOrWhiteSpace(host) && !host.Equals("https://github.com", StringComparison.OrdinalIgnoreCase))
		{
			args.Add("--host");
			args.Add(host);
		}

		_logger.LogInformation("Starting copilot login with args: {Args}", string.Join(" ", args));

		ProcessStartInfo psi = new("copilot")
		{
			Arguments = string.Join(" ", args),
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using Process? process = Process.Start(psi);
		if(process is null)
		{
			_logger.LogError("Failed to start copilot login process");
			return false;
		}

		string? deviceUrl = null;
		string? deviceCode = null;
		bool deviceFlowSent = false;

		void ParseOutput(string data)
		{
			Match urlMatch = DeviceUrlRegex().Match(data);
			Match codeMatch = DeviceCodeRegex().Match(data);

			if(urlMatch.Success)
			{
				deviceUrl = urlMatch.Groups[1].Value;
			}

			if(codeMatch.Success)
			{
				deviceCode = codeMatch.Groups[1].Value;
			}

			if(deviceUrl is not null && deviceCode is not null && !deviceFlowSent)
			{
				deviceFlowSent = true;
				_logger.LogInformation("Device flow received - URL: {Url}, Code: {Code}", deviceUrl, deviceCode);
				onDeviceFlow?.Invoke(new DeviceFlowInfo(deviceUrl, deviceCode));
			}
		}

		process.OutputDataReceived += (_, e) =>
		{
			if(e.Data is not null)
			{
				ParseOutput(e.Data);
			}
		};

		process.ErrorDataReceived += (_, e) =>
		{
			if(e.Data is not null)
			{
				ParseOutput(e.Data);
			}
		};

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		await process.WaitForExitAsync(cancellationToken);

		bool success = process.ExitCode == 0;
		_logger.LogInformation("copilot login exited with code {ExitCode}", process.ExitCode);
		return success;
	}

	/// <summary>
	/// Reads the last-used enterprise host from <c>~/.copilot/config.json</c> for pre-filling the UI.
	/// Returns <c>null</c> if not found or on any error.
	/// </summary>
	public static string? ReadHostFromConfig()
	{
		try
		{
			string configPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".copilot", "config.json");

			if(!File.Exists(configPath))
			{
				return null;
			}

			string json = File.ReadAllText(configPath);
			using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
			{
				CommentHandling = JsonCommentHandling.Skip
			});

			if(doc.RootElement.TryGetProperty("lastLoggedInUser", out JsonElement user) &&
				user.TryGetProperty("host", out JsonElement host))
			{
				return host.GetString();
			}
		}
		catch
		{
			// Fall through to default (github.com)
		}

		return null;
	}

	// Matches any host's device login URL (github.com or enterprise)
	[GeneratedRegex(@"(https://\S+/login/device)")]
	private static partial Regex DeviceUrlRegex();

	[GeneratedRegex(@"code\s+([A-Z0-9]{4}-[A-Z0-9]{4})")]
	private static partial Regex DeviceCodeRegex();
}
