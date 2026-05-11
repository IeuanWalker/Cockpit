using Cockpit.Features.MessageMode;
using Shouldly;

namespace Cockpit.UnitTests.Features.MessageMode;

/// <summary>
/// Tests for <see cref="MessageTurnModeExtensions"/>.
/// </summary>
public class MessageTurnModeExtensionsTests
{
	// -------------------------------------------------------------------------
	// SDK token constants
	// -------------------------------------------------------------------------

	[Fact]
	public void ImmediateSdkToken_HasExpectedValue()
	{
		MessageTurnModeExtensions.ImmediateSdkToken.ShouldBe("immediate");
	}

	[Fact]
	public void EnqueueSdkToken_HasExpectedValue()
	{
		MessageTurnModeExtensions.EnqueueSdkToken.ShouldBe("enqueue");
	}

	// -------------------------------------------------------------------------
	// ToSdkToken
	// -------------------------------------------------------------------------

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

	// -------------------------------------------------------------------------
	// FromSdkToken
	// -------------------------------------------------------------------------

	[Fact]
	public void FromSdkToken_ImmediateToken_ReturnsImmediate()
	{
		MessageTurnModeExtensions.FromSdkToken(MessageTurnModeExtensions.ImmediateSdkToken)
			.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	[Fact]
	public void FromSdkToken_EnqueueToken_ReturnsEnqueue()
	{
		MessageTurnModeExtensions.FromSdkToken(MessageTurnModeExtensions.EnqueueSdkToken)
			.ShouldBe(MessageTurnModeEnum.Enqueue);
	}

	[Fact]
	public void FromSdkToken_UnknownToken_FallsBackTo_Immediate()
	{
		MessageTurnModeExtensions.FromSdkToken("warp-speed")
			.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	[Fact]
	public void FromSdkToken_Null_FallsBackTo_Immediate()
	{
		MessageTurnModeExtensions.FromSdkToken(null)
			.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	[Fact]
	public void FromSdkToken_EmptyString_FallsBackTo_Immediate()
	{
		MessageTurnModeExtensions.FromSdkToken(string.Empty)
			.ShouldBe(MessageTurnModeEnum.Immediate);
	}

	// -------------------------------------------------------------------------
	// Roundtrip: ToSdkToken and FromSdkToken are inverses
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData(MessageTurnModeEnum.Immediate)]
	[InlineData(MessageTurnModeEnum.Enqueue)]
	public void RoundTrip_ToSdkToken_ThenFromSdkToken_PreservesMode(MessageTurnModeEnum mode)
	{
		string token = mode.ToSdkToken();
		MessageTurnModeExtensions.FromSdkToken(token).ShouldBe(mode);
	}
}
