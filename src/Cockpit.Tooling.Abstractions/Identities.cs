namespace Cockpit.Tooling;

public readonly record struct RuntimeSessionKey(string Value)
{
	public override string ToString() => Value;
}

public readonly record struct SessionStorageId(string Value)
{
	public override string ToString() => Value;
}

public readonly record struct WorkspaceToolInstanceId(string Value)
{
	public override string ToString() => Value;
}

public readonly record struct WorkspaceViewGeneration(long Value);

public sealed record WorkspaceSessionInfo(
	RuntimeSessionKey RuntimeSessionKey,
	SessionStorageId SessionStorageId,
	long IdentityEpoch,
	string Platform,
	string? WorkingDirectory,
	string? RepositoryRoot,
	string? SdkState,
	IReadOnlySet<string> Capabilities);
