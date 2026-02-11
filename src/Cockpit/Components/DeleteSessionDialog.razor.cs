using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class DeleteSessionDialog : ComponentBase
{
	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public string SessionTitle { get; set; } = string.Empty;
	[Parameter] public EventCallback OnConfirm { get; set; }
	[Parameter] public EventCallback OnCancel { get; set; }

	async Task Confirm()
	{
		await OnConfirm.InvokeAsync();
	}

	async Task Cancel()
	{
		await OnCancel.InvokeAsync();
	}
}
