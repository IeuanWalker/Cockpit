namespace Cockpit.Features.SystemMessage;

interface ISystemMessageFeature
{
	event Action? OnDefaultsLoaded;
	IReadOnlyDictionary<string, string> Defaults { get; }
	bool DefaultsLoaded { get; }
}
