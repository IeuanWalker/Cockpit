using System.Collections.Immutable;
using Cockpit.Tooling;

namespace Cockpit.Tools.Browser;

public sealed class BrowserToolInstance(WorkspaceToolInstanceId id)
	: WorkspaceToolInstance<BrowserState>(id, BrowserJsonContext.Default.BrowserState)
{
	public BrowserState ViewState => State;
	public BrowserPageState? Active => State.Tabs.FirstOrDefault(tab => tab.Id == State.ActiveTabId);
	public long RenderKey { get; private set; }
	public event Action? Changed;
	protected override int SchemaVersion => BrowserState.CurrentSchemaVersion;
	protected override BrowserState CreateDefaultState()
	{
		BrowserPageState tab = NewPage();
		return new() { Tabs = [tab], ActiveTabId = tab.Id };
	}
	public void AddTab()
	{
		BrowserPageState tab = NewPage();
		SetState(State with { Tabs = State.Tabs.Add(tab), ActiveTabId = tab.Id }); Notify();
	}
	public void Activate(string id) { if(State.Tabs.Any(tab => tab.Id == id)) { SetState(State with { ActiveTabId = id }); Notify(); } }
	public void Close(string id)
	{
		ImmutableArray<BrowserPageState> tabs = State.Tabs.RemoveAll(tab => tab.Id == id);
		if(tabs.Length == 0)
		{
			tabs = [NewPage()];
		}

		SetState(State with { Tabs = tabs, ActiveTabId = State.ActiveTabId == id ? tabs[0].Id : State.ActiveTabId }); Notify();
	}
	public void Navigate(string input)
	{
		BrowserPageState? active = Active;
		if(active is null)
		{
			return;
		}

		string url = Normalize(input);
		BrowserPageState next = active with { Url = url, Title = null, BackHistory = active.BackHistory.Add(active.Url), ForwardHistory = [] };
		Replace(next);
	}
	public void Back()
	{
		BrowserPageState? active = Active;
		if(active is null || active.BackHistory.Length == 0)
		{
			return;
		}

		string url = active.BackHistory[^1];
		Replace(active with { Url = url, BackHistory = active.BackHistory.RemoveAt(active.BackHistory.Length - 1), ForwardHistory = active.ForwardHistory.Insert(0, active.Url) });
	}
	public void Forward()
	{
		BrowserPageState? active = Active;
		if(active is null || active.ForwardHistory.Length == 0)
		{
			return;
		}

		string url = active.ForwardHistory[0];
		Replace(active with { Url = url, BackHistory = active.BackHistory.Add(active.Url), ForwardHistory = active.ForwardHistory.RemoveAt(0) });
	}
	public void Reload() { RenderKey++; Notify(); }
	void Replace(BrowserPageState page) { SetState(State with { Tabs = [.. State.Tabs.Select(tab => tab.Id == page.Id ? page : tab)] }); RenderKey++; Notify(); }
	void Notify() => Changed?.Invoke();
	static BrowserPageState NewPage() => new() { Id = Guid.NewGuid().ToString("N"), Url = "https://www.bing.com/" };
	static string Normalize(string value)
	{
		value = value.Trim();
		if(Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
		{
			return uri.AbsoluteUri;
		}

		if(!value.Contains(' ') && Uri.TryCreate("https://" + value, UriKind.Absolute, out uri))
		{
			return uri.AbsoluteUri;
		}

		return "https://www.bing.com/search?q=" + Uri.EscapeDataString(value);
	}
}
