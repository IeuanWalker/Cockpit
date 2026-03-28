using System.Text.Json;
using System.Text.Json.Serialization;
using Cockpit.Components.Controls;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class LicensesPopup : ComponentBase
{
	PopupBase _popup = default!;
	List<LibraryInfo> _libraries = [];

	protected override async Task OnInitializedAsync()
	{
		try
		{
			await using Stream stream = await FileSystem.OpenAppPackageFileAsync("libraries.json");
			_libraries = await JsonSerializer.DeserializeAsync(stream, LicensesJsonContext.Default.ListLibraryInfo) ?? [];
		}
		catch (Exception ex)
		{
			_libraries = [];
			Console.Error.WriteLine($"Failed to load or parse 'libraries.json' in {nameof(LicensesPopup)}: {ex}");
		}
	}

	public void Open()
	{
		_popup.Open();
	}

	static void OpenUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return;
		}

		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
		{
			return;
		}

		string scheme = uri.Scheme;
		if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
		    !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		_ = Launcher.Default.OpenAsync(uri);
	}
}

record LibraryInfo(string Name, string Version, string License, string? Url);

[JsonSerializable(typeof(List<LibraryInfo>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
partial class LicensesJsonContext : JsonSerializerContext { }
