namespace Cockpit.Features.SystemMessage;

public interface ISystemMessageFeature
{
	event Action? OnDefaultsLoaded;
	IReadOnlyDictionary<string, string> Defaults { get; }
	bool DefaultsLoaded { get; }
}
