using Cockpit.Features.Timestamp;
using Shouldly;

namespace Cockpit.UnitTests.Features.Timestamp;

public sealed class TimestampFeatureTests
{
	/// <summary>Frozen provider whose "now" can be set per-test.</summary>
	readonly ManualTimeProvider _time = new();
	readonly TimestampFeature _sut;

	public TimestampFeatureTests()
	{
		_sut = new TimestampFeature(_time);
	}

	// -----------------------------------------------------------------------
	// FormatRelative – happy-path rules
	// -----------------------------------------------------------------------

	[Fact]
	public void FormatRelative_LessThan60Seconds_ReturnsJustNow()
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime.AddSeconds(-59);

		string result = _sut.FormatRelative(input);

		result.ShouldBe("just now");
	}

	[Fact]
	public void FormatRelative_ExactlyZeroSecondsAgo_ReturnsJustNow()
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime;

		string result = _sut.FormatRelative(input);

		result.ShouldBe("just now");
	}

	[Fact]
	public void FormatRelative_FutureDate_ReturnsJustNow()
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime.AddMinutes(5);

		string result = _sut.FormatRelative(input);

		result.ShouldBe("just now");
	}

	[Theory]
	[InlineData(1, "1 minute ago")]
	[InlineData(2, "2 minutes ago")]
	[InlineData(59, "59 minutes ago")]
	public void FormatRelative_MinutesAgo_ReturnsMinuteString(int minutesAgo, string expected)
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime.AddMinutes(-minutesAgo);

		string result = _sut.FormatRelative(input);

		result.ShouldBe(expected);
	}

	[Theory]
	[InlineData(1, "1 hour ago")]
	[InlineData(2, "2 hours ago")]
	[InlineData(23, "23 hours ago")]
	public void FormatRelative_HoursAgo_ReturnsHourString(int hoursAgo, string expected)
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime.AddHours(-hoursAgo);

		string result = _sut.FormatRelative(input);

		result.ShouldBe(expected);
	}

	[Theory]
	[InlineData(1, "1 day ago")]
	[InlineData(2, "2 days ago")]
	[InlineData(6, "6 days ago")]
	public void FormatRelative_DaysAgo_ReturnsDayString(int daysAgo, string expected)
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime.AddDays(-daysAgo);

		string result = _sut.FormatRelative(input);

		result.ShouldBe(expected);
	}

	[Fact]
	public void FormatRelative_SevenOrMoreDays_SameYear_ReturnsShortDate()
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero));
		DateTime input = _time.GetUtcNow().UtcDateTime.AddDays(-7);

		string result = _sut.FormatRelative(input);

		// The exact string depends on local timezone, so we just verify it does NOT
		// look like a relative phrase and does look like a short date.
		result.ShouldNotContain("ago");
		result.ShouldNotBe("just now");
	}

	[Fact]
	public void FormatRelative_OlderThanOneYear_ReturneDateWithYear()
	{
		_time.SetUtcNow(new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero));
		DateTime input = new DateTime(2022, 3, 15, 0, 0, 0, DateTimeKind.Utc);

		string result = _sut.FormatRelative(input);

		result.ShouldContain("2022");
	}

	// -----------------------------------------------------------------------
	// FormatDuration – happy-path rules
	// -----------------------------------------------------------------------

	[Fact]
	public void FormatDuration_LessThanOneSecond_ReturnsLessThan1s()
	{
		DateTime start = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Local);
		DateTime end = start.AddMilliseconds(500);

		string result = _sut.FormatDuration(start, end);

		result.ShouldBe("<1s");
	}

	[Fact]
	public void FormatDuration_ExactlyOneSecond_ReturnsOneSecond()
	{
		DateTime start = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Local);
		DateTime end = start.AddSeconds(1);

		string result = _sut.FormatDuration(start, end);

		result.ShouldBe("1.0s");
	}

	[Fact]
	public void FormatDuration_TenPointFiveSeconds_ReturnsFormattedSeconds()
	{
		DateTime start = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Local);
		DateTime end = start.AddSeconds(10.5);

		string result = _sut.FormatDuration(start, end);

		result.ShouldBe("10.5s");
	}

	[Fact]
	public void FormatDuration_NinetySeconds_ReturnsMinutes()
	{
		DateTime start = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Local);
		DateTime end = start.AddSeconds(90);

		string result = _sut.FormatDuration(start, end);

		result.ShouldBe("1.5m");
	}

	[Fact]
	public void FormatDuration_SixtyMinutes_ReturnsHours()
	{
		DateTime start = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Local);
		DateTime end = start.AddMinutes(60);

		string result = _sut.FormatDuration(start, end);

		result.ShouldBe("1.0h");
	}

	[Fact]
	public void FormatDuration_NullEnd_UsesCurrentLocalTime()
	{
		// Arrange – set "now" so we can compute the expected value
		DateTimeOffset frozenNow = new DateTimeOffset(2024, 6, 1, 12, 0, 30, TimeSpan.Zero);
		_time.SetUtcNow(frozenNow);

		// start is 10 seconds before "now" in local time
		DateTime start = frozenNow.LocalDateTime.AddSeconds(-10);

		string result = _sut.FormatDuration(start, null);

		result.ShouldBe("10.0s");
	}

	// -----------------------------------------------------------------------
	// OnTick event
	// -----------------------------------------------------------------------

	[Fact]
	public async Task OnTick_FiresAtLeastOnce_WithinTwoSeconds()
	{
		int tickCount = 0;
		_sut.OnTick += () => Interlocked.Increment(ref tickCount);

		await Task.Delay(1500);

		tickCount.ShouldBeGreaterThanOrEqualTo(1);
	}
}

/// <summary>
/// A <see cref="TimeProvider"/> whose current time can be set programmatically,
/// enabling deterministic tests.
/// </summary>
sealed class ManualTimeProvider : TimeProvider
{
	DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

	public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

	public override DateTimeOffset GetUtcNow() => _utcNow;

	// GetLocalNow() is derived from GetUtcNow() + LocalTimeZone, so no override needed.
}
