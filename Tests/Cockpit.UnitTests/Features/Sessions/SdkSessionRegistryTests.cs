using Cockpit.Features.Sessions;
using GitHub.Copilot.SDK;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public class SdkSessionRegistryTests
{
	static SdkSessionRegistry CreateRegistry() => new();

	[Fact]
	public void TryGet_ReturnsFalse_WhenNotRegistered()
	{
		SdkSessionRegistry registry = CreateRegistry();

		bool found = registry.TryGet("nonexistent", out CopilotSession? session);

		found.ShouldBeFalse();
		session.ShouldBeNull();
	}

	[Fact]
	public void TryRemove_ReturnsFalse_WhenNotRegistered()
	{
		SdkSessionRegistry registry = CreateRegistry();

		bool removed = registry.TryRemove("nonexistent", out CopilotSession? session);

		removed.ShouldBeFalse();
		session.ShouldBeNull();
	}

	[Fact]
	public void Remove_IsNoOp_WhenNotRegistered()
	{
		SdkSessionRegistry registry = CreateRegistry();

		Should.NotThrow(() => registry.Remove("nonexistent"));
	}
}
