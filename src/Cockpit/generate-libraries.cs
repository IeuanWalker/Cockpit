#!/usr/bin/env dotnet-run
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

var projectFile = args.Length > 0 ? args[0] : "Cockpit.csproj";
var outputPath = Path.Combine("Resources", "Raw", "libraries.json");

var doc = XDocument.Load(projectFile);
XNamespace ns = doc.Root!.Name.Namespace;

var packageRefs = doc.Descendants(ns + "PackageReference")
    .Select(e => (
        Name: (string?)e.Attribute("Include"),
        Version: (string?)e.Attribute("Version")
    ))
    .Where(p => p.Name is not null && p.Version is not null)
    .ToList();

var nugetCache = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget", "packages");

var libraries = new List<LibraryInfo>();

foreach (var (name, version) in packageRefs)
{
    var nuspecPath = Path.Combine(nugetCache, name!.ToLowerInvariant(), version!, $"{name.ToLowerInvariant()}.nuspec");

    string? license = null;
    string? projectUrl = null;

    if (File.Exists(nuspecPath))
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

var json = JsonSerializer.Serialize(libraries, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
});

File.WriteAllText(outputPath, json);
Console.WriteLine($"Generated {outputPath} with {libraries.Count} libraries.");

record LibraryInfo(string Name, string Version, string License, string? Url);
