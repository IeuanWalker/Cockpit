using System.Windows.Input;

namespace Cockpit.Controls;

public partial class ToolbarButton : ContentView
{
	#region Commands

	public static readonly BindableProperty ClickedCommandProperty = BindableProperty.Create(nameof(ClickedCommand), typeof(ICommand), typeof(ToolbarButton), null, BindingMode.OneTime);

	public ICommand ClickedCommand
	{
		get => (ICommand)GetValue(ClickedCommandProperty);
		set => SetValue(ClickedCommandProperty, value);
	}

	/// <summary>
	/// Backing BindableProperty for the <see cref="ClickedCommandParameter"/> property.
	/// </summary>
	public static readonly BindableProperty ClickedCommandParameterProperty = BindableProperty.Create(nameof(ClickedCommandParameter), typeof(object), typeof(ToolbarButton));

	/// <summary>
	/// Property that gets returned when  <see cref="ClickedCommand" /> is executed. This is a bindable property.
	/// </summary>
	public object ClickedCommandParameter
	{
		get => GetValue(ClickedCommandParameterProperty);
		set => SetValue(ClickedCommandParameterProperty, value);
	}

	#endregion Commands

	public ToolbarButton()
	{
		InitializeComponent();
	}
}