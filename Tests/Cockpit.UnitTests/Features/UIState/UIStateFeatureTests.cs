using Cockpit.Features.AppSettings;
using Cockpit.Features.MessageMode;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.Theme;
using Cockpit.Features.UIState;
using Shouldly;

namespace Cockpit.UnitTests.Features.UIState;

public class UIStateFeatureTests
{
	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	static UIStateFeature CreateFeature(
		MockAppSettings? appSettings = null,
		MockTextToSpeech? tts = null)
	{
		return new UIStateFeature(
			tts ?? new MockTextToSpeech(),
			appSettings ?? new MockAppSettings());
	}

	// -------------------------------------------------------------------------
	// Constructor — loads initial values from IAppSettingsFeature
	// -------------------------------------------------------------------------

	[Fact]
	public void Constructor_LoadsLeftSidebarWidthFromSettings()
	{
		MockAppSettings settings = new() { LeftSidebarWidth = 300 };

		UIStateFeature feature = CreateFeature(settings);

		feature.LeftSidebarWidth.ShouldBe(300);
	}

	[Fact]
	public void Constructor_LoadsRightSidebarWidthFromSettings()
	{
		MockAppSettings settings = new() { RightSidebarWidth = 400 };

		UIStateFeature feature = CreateFeature(settings);

		feature.RightSidebarWidth.ShouldBe(400);
	}

	[Fact]
	public void Constructor_LoadsSendOnEnterFromSettings()
	{
		MockAppSettings settings = new() { SendOnEnter = true };

		UIStateFeature feature = CreateFeature(settings);

		feature.SendOnEnter.ShouldBeTrue();
	}

	[Fact]
	public void Constructor_LoadsTextToSpeechEnabledFromSettings()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = true };

		UIStateFeature feature = CreateFeature(settings);

		feature.TextToSpeechEnabled.ShouldBeTrue();
	}

	[Fact]
	public void Constructor_SidebarsStartExpanded()
	{
		UIStateFeature feature = CreateFeature();

		feature.LeftSidebarCollapsed.ShouldBeFalse();
		feature.RightSidebarCollapsed.ShouldBeFalse();
	}

	[Fact]
	public void Constructor_ClampsLeftSidebarWidthBelowMin()
	{
		MockAppSettings settings = new() { LeftSidebarWidth = 0 };

		UIStateFeature feature = CreateFeature(settings);

		feature.LeftSidebarWidth.ShouldBe(UIStateFeature.MinSidebarWidth);
	}

	[Fact]
	public void Constructor_ClampsLeftSidebarWidthAboveMax()
	{
		MockAppSettings settings = new() { LeftSidebarWidth = 9999 };

		UIStateFeature feature = CreateFeature(settings);

		feature.LeftSidebarWidth.ShouldBe(UIStateFeature.MaxSidebarWidth);
	}

	[Fact]
	public void Constructor_ClampsRightSidebarWidthBelowMin()
	{
		MockAppSettings settings = new() { RightSidebarWidth = 0 };

		UIStateFeature feature = CreateFeature(settings);

		feature.RightSidebarWidth.ShouldBe(UIStateFeature.MinSidebarWidth);
	}

	[Fact]
	public void Constructor_ClampsRightSidebarWidthAboveMax()
	{
		MockAppSettings settings = new() { RightSidebarWidth = 9999 };

		UIStateFeature feature = CreateFeature(settings);

		feature.RightSidebarWidth.ShouldBe(UIStateFeature.MaxSidebarWidth);
	}

	[Fact]
	public void Constructor_ClampedWidthPersistsBackToSettings()
	{
		MockAppSettings settings = new() { LeftSidebarWidth = 0 };

		UIStateFeature _ = CreateFeature(settings);

		// Clamped value should be written back so settings and feature agree
		settings.LeftSidebarWidth.ShouldBe(UIStateFeature.MinSidebarWidth);
	}

	// -------------------------------------------------------------------------
	// IUIStateFeature implementation
	// -------------------------------------------------------------------------

	[Fact]
	public void Feature_ImplementsInterface()
	{
		UIStateFeature feature = CreateFeature();

		feature.ShouldBeAssignableTo<IUIStateFeature>();
	}

	// -------------------------------------------------------------------------
	// ToggleLeftSidebar
	// -------------------------------------------------------------------------

	[Fact]
	public void ToggleLeftSidebar_CollapsesThenExpands()
	{
		UIStateFeature feature = CreateFeature();

		feature.ToggleLeftSidebar();
		feature.LeftSidebarCollapsed.ShouldBeTrue();

		feature.ToggleLeftSidebar();
		feature.LeftSidebarCollapsed.ShouldBeFalse();
	}

	[Fact]
	public void ToggleLeftSidebar_FiresOnStateChanged()
	{
		UIStateFeature feature = CreateFeature();
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.ToggleLeftSidebar();

		count.ShouldBe(1);
	}

	[Fact]
	public void ToggleLeftSidebar_AlwaysFiresEvent_EachToggle()
	{
		UIStateFeature feature = CreateFeature();
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.ToggleLeftSidebar();
		feature.ToggleLeftSidebar();

		count.ShouldBe(2);
	}

	// -------------------------------------------------------------------------
	// ToggleRightSidebar
	// -------------------------------------------------------------------------

	[Fact]
	public void ToggleRightSidebar_CollapsesThenExpands()
	{
		UIStateFeature feature = CreateFeature();

		feature.ToggleRightSidebar();
		feature.RightSidebarCollapsed.ShouldBeTrue();

		feature.ToggleRightSidebar();
		feature.RightSidebarCollapsed.ShouldBeFalse();
	}

	[Fact]
	public void ToggleRightSidebar_FiresOnStateChanged()
	{
		UIStateFeature feature = CreateFeature();
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.ToggleRightSidebar();

		count.ShouldBe(1);
	}

	[Fact]
	public void ToggleRightSidebar_AlwaysFiresEvent_EachToggle()
	{
		UIStateFeature feature = CreateFeature();
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.ToggleRightSidebar();
		feature.ToggleRightSidebar();

		count.ShouldBe(2);
	}

	// -------------------------------------------------------------------------
	// SetLeftSidebarWidth
	// -------------------------------------------------------------------------

	[Fact]
	public void SetLeftSidebarWidth_AcceptsValueWithinBounds()
	{
		UIStateFeature feature = CreateFeature();

		feature.SetLeftSidebarWidth(250);

		feature.LeftSidebarWidth.ShouldBe(250);
	}

	[Fact]
	public void SetLeftSidebarWidth_ClampsBelowMin()
	{
		UIStateFeature feature = CreateFeature();

		feature.SetLeftSidebarWidth(50);

		feature.LeftSidebarWidth.ShouldBe(UIStateFeature.MinSidebarWidth);
	}

	[Fact]
	public void SetLeftSidebarWidth_ClampsAboveMax()
	{
		UIStateFeature feature = CreateFeature();

		feature.SetLeftSidebarWidth(9999);

		feature.LeftSidebarWidth.ShouldBe(UIStateFeature.MaxSidebarWidth);
	}

	[Fact]
	public void SetLeftSidebarWidth_PersistsToAppSettings()
	{
		MockAppSettings settings = new();
		UIStateFeature feature = CreateFeature(settings);

		feature.SetLeftSidebarWidth(280);

		settings.LeftSidebarWidth.ShouldBe(280);
	}

	[Fact]
	public void SetLeftSidebarWidth_FiresOnStateChanged_WhenValueChanges()
	{
		MockAppSettings settings = new() { LeftSidebarWidth = 200 };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetLeftSidebarWidth(300);

		count.ShouldBe(1);
	}

	[Fact]
	public void SetLeftSidebarWidth_DoesNotFireOnStateChanged_WhenValueUnchanged()
	{
		MockAppSettings settings = new() { LeftSidebarWidth = 250 };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetLeftSidebarWidth(250);

		count.ShouldBe(0);
	}

	[Fact]
	public void SetLeftSidebarWidth_DoesNotFireOnStateChanged_WhenClampedToSameValue()
	{
		// Width is already at min; clamping 0 → MinSidebarWidth produces no change
		MockAppSettings settings = new() { LeftSidebarWidth = UIStateFeature.MinSidebarWidth };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetLeftSidebarWidth(0);

		count.ShouldBe(0);
	}

	// -------------------------------------------------------------------------
	// SetRightSidebarWidth
	// -------------------------------------------------------------------------

	[Fact]
	public void SetRightSidebarWidth_AcceptsValueWithinBounds()
	{
		UIStateFeature feature = CreateFeature();

		feature.SetRightSidebarWidth(350);

		feature.RightSidebarWidth.ShouldBe(350);
	}

	[Fact]
	public void SetRightSidebarWidth_ClampsBelowMin()
	{
		UIStateFeature feature = CreateFeature();

		feature.SetRightSidebarWidth(10);

		feature.RightSidebarWidth.ShouldBe(UIStateFeature.MinSidebarWidth);
	}

	[Fact]
	public void SetRightSidebarWidth_ClampsAboveMax()
	{
		UIStateFeature feature = CreateFeature();

		feature.SetRightSidebarWidth(1200);

		feature.RightSidebarWidth.ShouldBe(UIStateFeature.MaxSidebarWidth);
	}

	[Fact]
	public void SetRightSidebarWidth_PersistsToAppSettings()
	{
		MockAppSettings settings = new();
		UIStateFeature feature = CreateFeature(settings);

		feature.SetRightSidebarWidth(320);

		settings.RightSidebarWidth.ShouldBe(320);
	}

	[Fact]
	public void SetRightSidebarWidth_FiresOnStateChanged_WhenValueChanges()
	{
		MockAppSettings settings = new() { RightSidebarWidth = 200 };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetRightSidebarWidth(350);

		count.ShouldBe(1);
	}

	[Fact]
	public void SetRightSidebarWidth_DoesNotFireOnStateChanged_WhenValueUnchanged()
	{
		MockAppSettings settings = new() { RightSidebarWidth = 300 };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetRightSidebarWidth(300);

		count.ShouldBe(0);
	}

	[Fact]
	public void SetRightSidebarWidth_DoesNotFireOnStateChanged_WhenClampedToSameValue()
	{
		// Width is already at min; clamping 0 → MinSidebarWidth produces no change
		MockAppSettings settings = new() { RightSidebarWidth = UIStateFeature.MinSidebarWidth };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetRightSidebarWidth(0);

		count.ShouldBe(0);
	}

	// -------------------------------------------------------------------------
	// Width constants
	// -------------------------------------------------------------------------

	[Fact]
	public void MinSidebarWidth_Is150()
	{
		UIStateFeature.MinSidebarWidth.ShouldBe(150);
	}

	[Fact]
	public void MaxSidebarWidth_Is600()
	{
		UIStateFeature.MaxSidebarWidth.ShouldBe(600);
	}

	// -------------------------------------------------------------------------
	// SetEnterToSend
	// -------------------------------------------------------------------------

	[Fact]
	public void SetEnterToSend_SetsValueTrue()
	{
		MockAppSettings settings = new() { SendOnEnter = false };
		UIStateFeature feature = CreateFeature(settings);

		feature.SetEnterToSend(true);

		feature.SendOnEnter.ShouldBeTrue();
	}

	[Fact]
	public void SetEnterToSend_SetsValueFalse()
	{
		MockAppSettings settings = new() { SendOnEnter = true };
		UIStateFeature feature = CreateFeature(settings);

		feature.SetEnterToSend(false);

		feature.SendOnEnter.ShouldBeFalse();
	}

	[Fact]
	public void SetEnterToSend_PersistsToAppSettings()
	{
		MockAppSettings settings = new() { SendOnEnter = false };
		UIStateFeature feature = CreateFeature(settings);

		feature.SetEnterToSend(true);

		settings.SendOnEnter.ShouldBeTrue();
	}

	[Fact]
	public void SetEnterToSend_FiresOnStateChanged_WhenValueChanges()
	{
		MockAppSettings settings = new() { SendOnEnter = false };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetEnterToSend(true);

		count.ShouldBe(1);
	}

	[Fact]
	public void SetEnterToSend_DoesNotFireOnStateChanged_WhenValueUnchanged()
	{
		MockAppSettings settings = new() { SendOnEnter = true };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetEnterToSend(true);

		count.ShouldBe(0);
	}

	// -------------------------------------------------------------------------
	// SetTextToSpeechEnabled
	// -------------------------------------------------------------------------

	[Fact]
	public void SetTextToSpeechEnabled_SetsValueTrue()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = false };
		UIStateFeature feature = CreateFeature(settings);

		feature.SetTextToSpeechEnabled(true);

		feature.TextToSpeechEnabled.ShouldBeTrue();
	}

	[Fact]
	public void SetTextToSpeechEnabled_SetsValueFalse()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = true };
		UIStateFeature feature = CreateFeature(settings);

		feature.SetTextToSpeechEnabled(false);

		feature.TextToSpeechEnabled.ShouldBeFalse();
	}

	[Fact]
	public void SetTextToSpeechEnabled_PersistsToAppSettings()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = false };
		UIStateFeature feature = CreateFeature(settings);

		feature.SetTextToSpeechEnabled(true);

		settings.TextToSpeechEnabled.ShouldBeTrue();
	}

	[Fact]
	public void SetTextToSpeechEnabled_WhenDisabled_StopsTTS()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = true };
		MockTextToSpeech tts = new();
		UIStateFeature feature = CreateFeature(settings, tts);

		feature.SetTextToSpeechEnabled(false);

		tts.StopCallCount.ShouldBe(1);
	}

	[Fact]
	public void SetTextToSpeechEnabled_WhenEnabled_DoesNotStopTTS()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = false };
		MockTextToSpeech tts = new();
		UIStateFeature feature = CreateFeature(settings, tts);

		feature.SetTextToSpeechEnabled(true);

		tts.StopCallCount.ShouldBe(0);
	}

	[Fact]
	public void SetTextToSpeechEnabled_FiresOnStateChanged_WhenValueChanges()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = false };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetTextToSpeechEnabled(true);

		count.ShouldBe(1);
	}

	[Fact]
	public void SetTextToSpeechEnabled_DoesNotFireOnStateChanged_WhenValueUnchanged()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = true };
		UIStateFeature feature = CreateFeature(settings);
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.SetTextToSpeechEnabled(true);

		count.ShouldBe(0);
	}

	[Fact]
	public void SetTextToSpeechEnabled_DoesNotStopTTS_WhenValueUnchanged()
	{
		MockAppSettings settings = new() { TextToSpeechEnabled = false };
		MockTextToSpeech tts = new();
		UIStateFeature feature = CreateFeature(settings, tts);

		feature.SetTextToSpeechEnabled(false);

		tts.StopCallCount.ShouldBe(0);
	}

	// -------------------------------------------------------------------------
	// AppendChatInput
	// -------------------------------------------------------------------------

	[Fact]
	public void AppendChatInput_FiresOnAppendChatInput()
	{
		UIStateFeature feature = CreateFeature();
		string? received = null;
		feature.OnAppendChatInput += text => received = text;

		feature.AppendChatInput("hello world");

		received.ShouldBe("hello world");
	}

	[Fact]
	public void AppendChatInput_DoesNotFireOnStateChanged()
	{
		UIStateFeature feature = CreateFeature();
		int count = 0;
		feature.OnStateChanged += () => count++;

		feature.AppendChatInput("hello");

		count.ShouldBe(0);
	}

	[Fact]
	public void AppendChatInput_WhenNoSubscribers_DoesNotThrow()
	{
		UIStateFeature feature = CreateFeature();

		Should.NotThrow(() => feature.AppendChatInput("test"));
	}

	[Fact]
	public void AppendChatInput_EmptyString_StillFiresEvent()
	{
		UIStateFeature feature = CreateFeature();
		string? received = null;
		feature.OnAppendChatInput += text => received = text;

		feature.AppendChatInput(string.Empty);

		received.ShouldBe(string.Empty);
	}

	[Fact]
	public void AppendChatInput_MultipleSubscribers_AllReceiveText()
	{
		UIStateFeature feature = CreateFeature();
		List<string> received = [];
		feature.OnAppendChatInput += text => received.Add("a:" + text);
		feature.OnAppendChatInput += text => received.Add("b:" + text);

		feature.AppendChatInput("hello");

		received.ShouldBe(["a:hello", "b:hello"]);
	}
}

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

sealed class MockTextToSpeech : ITextToSpeechFeature
{
#pragma warning disable CS0067 // Events are never used; required to satisfy interface
	public event Action? OnStateChanged;
	public string? ActiveMessageId => null;
	public bool IsSpeaking => false;
	public int StopCallCount { get; private set; }

	public Task<IEnumerable<Locale>> GetLocales() => Task.FromResult<IEnumerable<Locale>>([]);
	public Task Speak(string messageId, string text) => Task.CompletedTask;

	public Task Stop()
	{
		StopCallCount++;
		return Task.CompletedTask;
	}

	public void Dispose() { }
#pragma warning restore CS0067
}

sealed class MockAppSettings : IAppSettingsFeature
{
	public ThemeEnum Theme { get; set; }
	public string AccentColor { get; set; } = string.Empty;
	public string AccentHoverColor { get; set; } = string.Empty;
	public MessageTurnModeEnum MessageTurnMode { get; set; }
	public bool SendOnEnter { get; set; }
	public int LeftSidebarWidth { get; set; }
	public int RightSidebarWidth { get; set; }
	public bool TextToSpeechEnabled { get; set; }
	public float VoiceVolume { get; set; }
	public float VoicePitch { get; set; }
	public float VoiceRate { get; set; }
	public string VoiceLocale { get; set; } = string.Empty;
	public bool DiffSplitView { get; set; }
	public bool DiffTreeView { get; set; }
	public bool SoundPermissionEnabled { get; set; }
	public float SoundPermissionVolume { get; set; } = 0.5f;
	public bool SoundUserInputEnabled { get; set; }
	public float SoundUserInputVolume { get; set; } = 0.5f;
	public bool SoundFinishedEnabled { get; set; }
	public float SoundFinishedVolume { get; set; } = 0.5f;
	public string SoundPermissionCustomFileName { get; set; } = string.Empty;
	public string SoundUserInputCustomFileName { get; set; } = string.Empty;
	public string SoundFinishedCustomFileName { get; set; } = string.Empty;
	public bool KeepAlive { get; set; }
	public bool CanvasEnabled { get; set; } = true;
	public string SessionListGroupBy { get; set; } = string.Empty;
	public Dictionary<string, Cockpit.Features.SystemMessage.SystemMessageSectionSetting> SystemMessageSectionOverrides { get; set; } = [];
}
