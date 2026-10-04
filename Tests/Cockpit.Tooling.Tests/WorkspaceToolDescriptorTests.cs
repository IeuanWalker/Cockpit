namespace Cockpit.Tooling.Tests;

public sealed class WorkspaceToolDescriptorTests
{
	[Fact]
	public void Validate_accepts_external_id_and_does_not_define_instance_limits()
	{
		WorkspaceToolDescriptor descriptor = TestTools.Descriptor("example.notes");

		Assert.Empty(descriptor.Validate());
		Assert.DoesNotContain(
			descriptor.GetType().GetProperties(),
			property => property.Name.Contains("Maximum", StringComparison.OrdinalIgnoreCase) ||
						property.Name.Contains("Singleton", StringComparison.OrdinalIgnoreCase));
	}

	[Theory]
	[InlineData("Example.Notes")]
	[InlineData("example notes")]
	[InlineData("")]
	public void Validate_rejects_unstable_ids(string id)
	{
		Assert.NotEmpty(TestTools.Descriptor(id).Validate());
	}
}
