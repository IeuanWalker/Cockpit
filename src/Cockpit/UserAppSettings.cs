namespace Cockpit;

public static class UserAppSettings
{
	public static ThemeEnum Theme
	{
		get
		{
			string? result = Preferences.Default.Get("Theme", (string?)null);

			if(result is null)
			{
				return ThemeEnum.Dark;
			}

			if(!Enum.IsDefined(typeof(ThemeEnum), result))
			{
				return ThemeEnum.Dark;
			}

			return Enum.Parse<ThemeEnum>(result);
		}

		set => Preferences.Default.Set("Theme", value.ToString());
	}
	public static string AccentColor
	{
		get => Preferences.Default.Get("AccentColor", "#005FB8");
		set => Preferences.Default.Set("AccentColor", value);
	}
	public static string AccentHoverColor
	{
		get => Preferences.Default.Get("AccentHoverColor", "#0050a0");
		set => Preferences.Default.Set("AccentHoverColor", value);
	}

	public static bool SendOnEnter
	{
		get => Preferences.Default.Get("SendOnEnter", true);
		set => Preferences.Default.Set("SendOnEnter", value);
	}

	public static int LeftSidebarWidth
	{
		get => Preferences.Default.Get("LeftSidebarWidth", 224);
		set => Preferences.Default.Set("LeftSidebarWidth", value);
	}

	public static int RightSidebarWidth
	{
		get => Preferences.Default.Get("RightSidebarWidth", 256);
		set => Preferences.Default.Set("RightSidebarWidth", value);
	}
}

// TODO: Implement system
public enum ThemeEnum
{
	Light,
	Dark,
}
