using System.Text;
using Cockpit.Components.Controls;
using Cockpit.Utilities.Logging;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class ReportIssuePopup : ComponentBase
{
	const string IssuesUrl = "https://github.com/IeuanWalker/Cockpit/issues/new?title=&body=";
	const int MaxUrlLength = 8202;

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
		int usedLength = IssuesUrl.Length
		+ Uri.EscapeDataString(_title).Length
		+ Uri.EscapeDataString(header + footer).Length;

		int logBudget = MaxUrlLength - usedLength;

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
		int budgetPerFile = urlBudget / fileCount;

		StringBuilder sb = new();
		if(appLines.Length > 0)
		{
			AppendFittedLog(sb, appLines, "app.log", budgetPerFile);
		}

		if(crashLines.Length > 0)
		{
			AppendFittedLog(sb, crashLines, "crash.log", budgetPerFile);
		}

		return sb.ToString();
	}

	static void AppendFittedLog(StringBuilder sb, string[] allLines, string name, int urlBudget)
	{
		// Measure the encoded overhead for the section wrapper
		string sectionHeader = $"## {name}\n```\n";
		string sectionFooter = "\n```\n\n";
		int overhead = Uri.EscapeDataString(sectionHeader + sectionFooter).Length
		+ Uri.EscapeDataString($"... (showing last 9999 of {allLines.Length} lines)\n").Length;

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
			selected.Insert(0, allLines[i]);
		}

		if(selected.Count == 0)
		{
			return;
		}

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
			return sr.ReadToEnd().Split('\n');
		}
		catch { return []; }
	}

	bool CanSubmit => !string.IsNullOrWhiteSpace(_title);
}
