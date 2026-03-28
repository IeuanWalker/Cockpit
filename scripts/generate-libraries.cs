using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

if(args.Length < 2)
{
	Console.Error.WriteLine("Usage: generate-libraries.cs <path-to.csproj> <output-path>");
	return 1;
}

var csprojPath = args[0];
var outputPath = args[1];

// Resolve the NuGet global packages path
var nugetCache = await GetNuGetGlobalPackagesPath();
if(nugetCache is null)
{
	Console.Error.WriteLine("Error: could not determine NuGet global packages path.");
	return 1;
}
nugetCache = nugetCache.TrimEnd('\\', '/');

var doc = XDocument.Load(csprojPath);
XNamespace ns = doc.Root!.Name.Namespace;

var packageRefs = doc.Descendants(ns + "PackageReference")
	.Select(e => (
		Name: (string?)e.Attribute("Include"),
		Version: (string?)e.Attribute("Version")
	))
	.Where(p => p.Name is not null && p.Version is not null)
	.ToList();

var libraries = new List<LibraryInfo>();

foreach(var (name, version) in packageRefs)
{
	var nuspecPath = Path.Combine(
		nugetCache,
		name!.ToLowerInvariant(),
		version!,
		$"{name.ToLowerInvariant()}.nuspec");

	string? license = null;
	string? projectUrl = null;

	if(File.Exists(nuspecPath))
	{
		var nuspec = XDocument.Load(nuspecPath);
		XNamespace nuspecNs = nuspec.Root!.Name.Namespace;
		var metadata = nuspec.Root.Element(nuspecNs + "metadata");

		// Prefer <license type="expression"> (SPDX), fall back to <licenseUrl>
		var licenseEl = metadata?.Element(nuspecNs + "license");
		license = licenseEl is not null
			? licenseEl.Value
			: metadata?.Element(nuspecNs + "licenseUrl")?.Value;

		projectUrl = metadata?.Element(nuspecNs + "projectUrl")?.Value;
	}
	else
	{
		Console.Error.WriteLine($"Warning: nuspec not found for {name} {version} at {nuspecPath}");
	}

	libraries.Add(new LibraryInfo(name!, version!, license ?? "Unknown", projectUrl));
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var json = JsonSerializer.Serialize(libraries, LibraryInfoContext.Default.ListLibraryInfo);
File.WriteAllText(outputPath, json);
Console.WriteLine($"Generated {outputPath} with {libraries.Count} libraries.");

return 0;

static async Task<string?> GetNuGetGlobalPackagesPath()
{
	var psi = new ProcessStartInfo("dotnet", "nuget locals global-packages --list")
	{
		RedirectStandardOutput = true,
		UseShellExecute = false,
	};
	using var process = Process.Start(psi)!;
	var output = await process.StandardOutput.ReadToEndAsync();
	await process.WaitForExitAsync();
	// Output: "global-packages: D:\Packages\.nuget\packages\"
	var parts = output.Split(':', 2);
	return parts.Length == 2 ? parts[1].Trim() : null;
}

record LibraryInfo(string Name, string Version, string License, string? Url);

[JsonSourceGenerationOptions(
	WriteIndented = true,
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<LibraryInfo>))]
internal partial class LibraryInfoContext : JsonSerializerContext { }