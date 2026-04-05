using System.Text;
using Cockpit.Components.Controls;
using Cockpit.Utilities.Logging;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class ReportIssuePopup : ComponentBase
{
	const string issuesUrl = "https://github.com/IeuanWalker/Cockpit/issues/new?title=&body=";
	const int maxUrlLength = 8202;

	public const int MaxTitleLength = 150;
	public const int MaxDescriptionLength = 1000;
	public const int MaxStepsLength = 1000;

	PopupBase _popup = default!;

	string _title = string.Empty;
	string _description = string.Empty;
	string _stepsToReproduce = string.Empty;
	bool _includeLogs = true;

	public void Open()
	{
		_title = string.Empty;
		_description = string.Empty;
		_stepsToReproduce = string.Empty;
		_includeLogs = true;
		_popup.Open();
		StateHasChanged();
	}

	void Cancel() => _popup.Close();

	async Task Submit()
	{
		string body = BuildBody();
		string url = $"https://github.com/IeuanWalker/Cockpit/issues/new?title={Uri.EscapeDataString(_title)}&body={Uri.EscapeDataString(body)}";
		await Launcher.Default.OpenAsync(new Uri(url));
		_popup.Close();
	}

	string BuildBody()
	{
		string header = BuildHeader();
		string footer = BuildFooter();

		if(!_includeLogs)
		{
			return header + footer;
		}

		// Calculate remaining URL chars after fixed content is accounted for
		int usedLength = issuesUrl.Length
		+ Uri.EscapeDataString(_title).Length
		+ Uri.EscapeDataString(header + footer).Length;

		int logBudget = maxUrlLength - usedLength;

		string logSection = BuildLogSection(logBudget);
		return header + logSection + footer;
	}

	string BuildHeader()
	{
		StringBuilder sb = new();

		if(!string.IsNullOrWhiteSpace(_description))
		{
			sb.AppendLine("## Description");
			sb.AppendLine(_description.Trim());
			sb.AppendLine();
		}

		if(!string.IsNullOrWhiteSpace(_stepsToReproduce))
		{
			sb.AppendLine("## Steps to Reproduce");
			sb.AppendLine(_stepsToReproduce.Trim());
			sb.AppendLine();
		}

		return sb.ToString();
	}

	static string BuildFooter()
	{
		StringBuilder sb = new();
		sb.AppendLine("## Environment");
		sb.AppendLine($"- OS: {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}");
		sb.AppendLine($"- Version: {AppInfo.Current.VersionString}");
		return sb.ToString();
	}

	static string BuildLogSection(int urlBudget)
	{
		if(urlBudget < 100)
		{
			return string.Empty;
		}

		string logDir = LogDirectoryHelper.LogDirectory;
		string[] appLines = ReadLogLines(Path.Combine(logDir, "app.log"));
		string[] crashLines = ReadLogLines(Path.Combine(logDir, "crash.log"));

		if(appLines.Length == 0 && crashLines.Length == 0)
		{
			return string.Empty;
		}

		int fileCount = (appLines.Length > 0 ? 1 : 0) + (crashLines.Length > 0 ? 1 : 0);
		int budgetPerFile = urlBudget / Math.Max(fileCount, 1);

		StringBuilder sb = new();
		int remainingBudget = urlBudget;

		// Add crash log first, filtered to last 3 hours, up to budgetPerFile
		if(crashLines.Length > 0)
		{
			string[] recentCrashLines = FilterCrashLogLastHours(crashLines, 3);
			if(recentCrashLines.Length > 0)
			{
				int beforeLength = sb.Length;
				AppendFittedLog(sb, recentCrashLines, "crash.log", budgetPerFile);
				int crashUsed = Uri.EscapeDataString(sb.ToString(beforeLength, sb.Length - beforeLength)).Length;
				remainingBudget -= crashUsed;
			}
			else
			{
				// No recent crash lines; full budget available for app.log
				remainingBudget = urlBudget;
			}
		}

		// app.log gets whatever budget is left (at least budgetPerFile)
		if(appLines.Length > 0)
		{
			int appBudget = Math.Max(remainingBudget, budgetPerFile);
			AppendFittedLog(sb, appLines, "app.log", appBudget);
		}

		return sb.ToString();
	}

	static string[] FilterCrashLogLastHours(string[] lines, int hours)
	{
		DateTime cutoff = DateTime.Now.AddHours(-hours);

		// Crash log sections start with === yyyy-MM-dd HH:mm:ss [Source] ===
		// Collect indices of section headers that are within the time window
		HashSet<int> includedSections = [];
		int? currentSection = null;

		for(int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if(line.StartsWith("===") && line.EndsWith("===") && line.Length > 6)
			{
				currentSection = i;
				// Parse timestamp: === yyyy-MM-dd HH:mm:ss [Source] ===
				string inner = line[3..^3].Trim(); // strip leading/trailing ===
				int bracketPos = inner.IndexOf('[');
				string tsPart = bracketPos > 0 ? inner[..bracketPos].Trim() : inner;
				if(DateTime.TryParse(tsPart, out DateTime ts) && ts >= cutoff)
				{
					includedSections.Add(i);
				}
			}
			else if(currentSection.HasValue && includedSections.Contains(currentSection.Value))
			{
				includedSections.Add(i);
			}
		}

		return includedSections.Count == 0
			? []
			: lines.Where((_, i) => includedSections.Contains(i)).ToArray();
	}

	static void AppendFittedLog(StringBuilder sb, string[] allLines, string name, int urlBudget)
	{
		// Measure the encoded overhead for the section wrapper, using the maximum possible
		// truncation-notice length to avoid underestimating the budget.
		string sectionHeader = $"## {name}\n```\n";
		string sectionFooter = "\n```\n\n";
		int overhead = Uri.EscapeDataString(sectionHeader + sectionFooter).Length
		+ Uri.EscapeDataString($"... (showing last {allLines.Length} of {allLines.Length} lines)\n").Length;

		int contentBudget = urlBudget - overhead;
		if(contentBudget <= 0)
		{
			return;
		}

		// Greedily take lines from the tail until the encoded budget is exhausted
		List<string> selected = [];
		int used = 0;

		for(int i = allLines.Length - 1; i >= 0; i--)
		{
			int encodedLen = Uri.EscapeDataString(allLines[i] + "\n").Length;
			if(used + encodedLen > contentBudget)
			{
				break;
			}

			used += encodedLen;
			selected.Add(allLines[i]);
		}

		if(selected.Count == 0)
		{
			return;
		}

		selected.Reverse();

		sb.AppendLine($"## {name}");
		sb.AppendLine("```");
		if(selected.Count < allLines.Length)
		{
			sb.AppendLine($"... (showing last {selected.Count} of {allLines.Length} lines)");
		}

		sb.AppendLine(string.Join("\n", selected).TrimEnd());
		sb.AppendLine("```");
		sb.AppendLine();
	}

	static string[] ReadLogLines(string path)
	{
		try
		{
			if(!File.Exists(path))
			{
				return [];
			}

			using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader sr = new(fs);
			return sr.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		}
		catch { return []; }
	}

	bool CanSubmit => !string.IsNullOrWhiteSpace(_title);
}
