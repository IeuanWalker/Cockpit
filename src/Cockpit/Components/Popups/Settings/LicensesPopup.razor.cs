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
		catch
		{
			_libraries = [];
		}
	}

	public void Open()
	{
		_popup.Open();
	}

	static void OpenUrl(string url)
	{
		_ = Launcher.Default.OpenAsync(new Uri(url));
	}
}

record LibraryInfo(string Name, string Version, string License, string? Url);

[JsonSerializable(typeof(List<LibraryInfo>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
partial class LicensesJsonContext : JsonSerializerContext { }
