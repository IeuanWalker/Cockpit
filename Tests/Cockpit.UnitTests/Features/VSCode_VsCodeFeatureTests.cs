using System.Diagnostics;
using Cockpit.Features.VSCode;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features;

public class VsCodeFeatureTests
{
	static VsCodeFeature CreateFeature() => new(NullLogger<VsCodeFeature>.Instance);

	static VsCodeFeature CreateFeatureWithAvailability(bool isAvailable, IProcessLauncher? launcher = null)
	{
		string? executablePath = isAvailable ? "code" : null;
		return new VsCodeFeature(NullLogger<VsCodeFeature>.Instance, executablePath, launcher);
	}

	// ---------------------------------------------------------------------------
	// Construction
	// ---------------------------------------------------------------------------

	[Fact]
	public void Constructor_DoesNotThrow()
	{
		Should.NotThrow(CreateFeature);
	}

	[Fact]
	public void IsAvailable_IsAccessible_AfterConstruction()
	{
		VsCodeFeature feature = CreateFeature();
		bool _ = feature.IsAvailable; // should not throw
	}

	// ---------------------------------------------------------------------------
	// BuildOpenArguments
	// ---------------------------------------------------------------------------

	[Theory]
	[InlineData(@"C:\some\path")]
	[InlineData(@"C:\path with spaces\my project")]
	[InlineData(@"/home/user/project")]
	[InlineData("single")]
	public void BuildOpenArguments_ReturnsSingleElementEqualToPath(string path)
	{
		IReadOnlyList<string> args = VsCodeFeature.BuildOpenArguments(path);

		args.Count.ShouldBe(1);
		args[0].ShouldBe(path);
	}

	// ---------------------------------------------------------------------------
	// BuildGotoArguments
	// ---------------------------------------------------------------------------

	[Theory]
	[InlineData(@"C:\src\file.cs", 1)]
	[InlineData(@"C:\src\file.cs", 42)]
	[InlineData(@"/home/user/file.py", 100)]
	public void BuildGotoArguments_StartsWithGotoFlag(string filePath, int line)
	{
		IReadOnlyList<string> args = VsCodeFeature.BuildGotoArguments(filePath, line);

		args[0].ShouldBe("--goto");
	}

	[Theory]
	[InlineData(@"C:\src\file.cs", 42)]
	[InlineData(@"/home/user/file.py", 100)]
	public void BuildGotoArguments_SecondElementContainsPathAndLine(string filePath, int line)
	{
		IReadOnlyList<string> args = VsCodeFeature.BuildGotoArguments(filePath, line);

		args[1].ShouldBe($"{filePath}:{line}");
	}

	[Fact]
	public void BuildGotoArguments_ReturnsTwoElements()
	{
		IReadOnlyList<string> args = VsCodeFeature.BuildGotoArguments(@"C:\file.cs", 5);

		args.Count.ShouldBe(2);
	}

	// ---------------------------------------------------------------------------
	// OpenPathInVsCode – unavailable
	// ---------------------------------------------------------------------------

	[Fact]
	public void OpenPathInVsCode_ReturnsFalse_WhenNotAvailable()
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		bool result = feature.OpenPathInVsCode(@"C:\some\path");

		result.ShouldBeFalse();
	}

	[Theory]
	[InlineData(@"C:\some\path")]
	[InlineData(@"/home/user/project")]
	[InlineData("")]
	[InlineData("   ")]
	public void OpenPathInVsCode_ReturnsFalse_WhenNotAvailable_ForAnyPath(string path)
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		bool result = feature.OpenPathInVsCode(path);

		result.ShouldBeFalse();
	}

	[Fact]
	public void OpenPathInVsCode_DoesNotThrow_WhenNotAvailable()
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		Should.NotThrow(() => feature.OpenPathInVsCode(@"C:\some\path"));
	}

	// ---------------------------------------------------------------------------
	// OpenPathInVsCode – input validation
	// ---------------------------------------------------------------------------

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t")]
	public void OpenPathInVsCode_ReturnsFalse_ForNullOrWhiteSpacePath(string path)
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenPathInVsCode(path);

		result.ShouldBeFalse();
		launcher.StartCallCount.ShouldBe(0);
	}

	// ---------------------------------------------------------------------------
	// OpenPathInVsCode – successful launch
	// ---------------------------------------------------------------------------

	[Fact]
	public void OpenPathInVsCode_ReturnsTrue_WhenAvailableAndPathValid()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenPathInVsCode(@"C:\some\path");

		result.ShouldBeTrue();
	}

	[Fact]
	public void OpenPathInVsCode_InvokesLauncher_WhenAvailableAndPathValid()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		feature.OpenPathInVsCode(@"C:\some\path");

		launcher.StartCallCount.ShouldBe(1);
	}

	[Fact]
	public void OpenPathInVsCode_PassesCorrectArguments_ToLauncher()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);
		string path = @"C:\path with spaces\project";

		feature.OpenPathInVsCode(path);

		launcher.LastStartInfo.ShouldNotBeNull();
		launcher.LastStartInfo!.ArgumentList.ShouldContain(path);
		launcher.LastStartInfo.ArgumentList.Count.ShouldBe(1);
	}

	[Fact]
	public void OpenPathInVsCode_UsesCorrectExecutable()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		feature.OpenPathInVsCode(@"C:\project");

		launcher.LastStartInfo.ShouldNotBeNull();
		launcher.LastStartInfo!.FileName.ShouldBe("code");
	}

	[Fact]
	public void OpenPathInVsCode_DoesNotUseShellExecute()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		feature.OpenPathInVsCode(@"C:\project");

		launcher.LastStartInfo.ShouldNotBeNull();
		launcher.LastStartInfo!.UseShellExecute.ShouldBeFalse();
	}

	// ---------------------------------------------------------------------------
	// OpenPathInVsCode – launcher failure
	// ---------------------------------------------------------------------------

	[Fact]
	public void OpenPathInVsCode_ReturnsFalse_WhenLauncherReturnsNull()
	{
		NullProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenPathInVsCode(@"C:\some\path");

		result.ShouldBeFalse();
	}

	[Fact]
	public void OpenPathInVsCode_ReturnsFalse_WhenLauncherThrows()
	{
		ThrowingProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenPathInVsCode(@"C:\some\path");

		result.ShouldBeFalse();
	}

	[Fact]
	public void OpenPathInVsCode_DoesNotThrow_WhenLauncherThrows()
	{
		ThrowingProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		Should.NotThrow(() => feature.OpenPathInVsCode(@"C:\some\path"));
	}

	// ---------------------------------------------------------------------------
	// OpenFileAtLineInVsCode – unavailable
	// ---------------------------------------------------------------------------

	[Fact]
	public void OpenFileAtLineInVsCode_ReturnsFalse_WhenNotAvailable()
	{
		VsCodeFeature feature = CreateFeatureWithAvailability(false);

		bool result = feature.OpenFileAtLineInVsCode(@"C:\file.cs", 42);

		result.ShouldBeFalse();
	}

	// ---------------------------------------------------------------------------
	// OpenFileAtLineInVsCode – input validation
	// ---------------------------------------------------------------------------

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t")]
	public void OpenFileAtLineInVsCode_ReturnsFalse_ForNullOrWhiteSpacePath(string filePath)
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenFileAtLineInVsCode(filePath, 1);

		result.ShouldBeFalse();
		launcher.StartCallCount.ShouldBe(0);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-100)]
	public void OpenFileAtLineInVsCode_ReturnsFalse_ForInvalidLineNumber(int line)
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenFileAtLineInVsCode(@"C:\file.cs", line);

		result.ShouldBeFalse();
		launcher.StartCallCount.ShouldBe(0);
	}

	// ---------------------------------------------------------------------------
	// OpenFileAtLineInVsCode – successful launch
	// ---------------------------------------------------------------------------

	[Fact]
	public void OpenFileAtLineInVsCode_ReturnsTrue_WhenAvailableAndInputsValid()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenFileAtLineInVsCode(@"C:\file.cs", 42);

		result.ShouldBeTrue();
	}

	[Fact]
	public void OpenFileAtLineInVsCode_InvokesLauncher_WhenAvailableAndInputsValid()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		feature.OpenFileAtLineInVsCode(@"C:\file.cs", 42);

		launcher.StartCallCount.ShouldBe(1);
	}

	[Fact]
	public void OpenFileAtLineInVsCode_PassesGotoFlag_ToLauncher()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		feature.OpenFileAtLineInVsCode(@"C:\file.cs", 42);

		launcher.LastStartInfo.ShouldNotBeNull();
		launcher.LastStartInfo!.ArgumentList.ShouldContain("--goto");
	}

	[Fact]
	public void OpenFileAtLineInVsCode_PassesFilePathAndLine_ToLauncher()
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);
		string filePath = @"C:\file.cs";
		int line = 42;

		feature.OpenFileAtLineInVsCode(filePath, line);

		launcher.LastStartInfo.ShouldNotBeNull();
		launcher.LastStartInfo!.ArgumentList.ShouldContain($"{filePath}:{line}");
	}

	[Theory]
	[InlineData(1)]
	[InlineData(42)]
	[InlineData(9999)]
	public void OpenFileAtLineInVsCode_AcceptsAnyPositiveLine(int line)
	{
		FakeProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenFileAtLineInVsCode(@"C:\file.cs", line);

		result.ShouldBeTrue();
	}

	// ---------------------------------------------------------------------------
	// OpenFileAtLineInVsCode – launcher failure
	// ---------------------------------------------------------------------------

	[Fact]
	public void OpenFileAtLineInVsCode_ReturnsFalse_WhenLauncherReturnsNull()
	{
		NullProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenFileAtLineInVsCode(@"C:\file.cs", 1);

		result.ShouldBeFalse();
	}

	[Fact]
	public void OpenFileAtLineInVsCode_ReturnsFalse_WhenLauncherThrows()
	{
		ThrowingProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		bool result = feature.OpenFileAtLineInVsCode(@"C:\file.cs", 1);

		result.ShouldBeFalse();
	}

	[Fact]
	public void OpenFileAtLineInVsCode_DoesNotThrow_WhenLauncherThrows()
	{
		ThrowingProcessLauncher launcher = new();
		VsCodeFeature feature = CreateFeatureWithAvailability(true, launcher);

		Should.NotThrow(() => feature.OpenFileAtLineInVsCode(@"C:\file.cs", 1));
	}
}

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

sealed class FakeProcessLauncher : IProcessLauncher
{
	public ProcessStartInfo? LastStartInfo { get; private set; }
	public int StartCallCount { get; private set; }

	public Process? Start(ProcessStartInfo startInfo)
	{
		LastStartInfo = startInfo;
		StartCallCount++;
		return new Process();
	}
}

sealed class NullProcessLauncher : IProcessLauncher
{
	public Process? Start(ProcessStartInfo startInfo) => null;
}

sealed class ThrowingProcessLauncher : IProcessLauncher
{
	public Process? Start(ProcessStartInfo startInfo) =>
		throw new InvalidOperationException("Simulated launch failure");
}
