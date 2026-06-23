using Cockpit.Features.MessageMode;
using Shouldly;

namespace Cockpit.UnitTests.Features.MessageMode;

/// <summary>
/// Tests for <see cref="MessageTurnModeExtensions"/>.
/// </summary>
public class MessageTurnModeExtensionsTests
{
	[Fact]
	public void ToSdkToken_Immediate_ReturnsImmediateToken()
	{
		MessageTurnModeEnum.Immediate.ToSdkToken().ShouldBe(MessageTurnModeExtensions.ImmediateSdkToken);
	}

	[Fact]
	public void ToSdkToken_Enqueue_ReturnsEnqueueToken()
	{
		MessageTurnModeEnum.Enqueue.ToSdkToken().ShouldBe(MessageTurnModeExtensions.EnqueueSdkToken);
	}
}
