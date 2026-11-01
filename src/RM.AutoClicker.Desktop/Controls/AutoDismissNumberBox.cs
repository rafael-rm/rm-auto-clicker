using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace RM.AutoClicker.Desktop.Controls;

public sealed class AutoDismissNumberBox : NumberBox
{
    private Popup? _spinPopup;

    public AutoDismissNumberBox()
    {
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => OpenSpinPopup()), true);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _spinPopup = GetTemplateChild("UpDownPopup") as Popup;
    }

    internal void OpenSpinPopup()
    {
        if (IsEnabled && SpinButtonPlacementMode == NumberBoxSpinButtonPlacementMode.Compact && _spinPopup is { } popup)
            popup.IsOpen = true;
    }

    internal void DismissSpinPopup()
    {
        if (_spinPopup is not { IsOpen: true } popup)
            return;
        if (GetTemplateChild("InputBox") is TextBox editor)
            Text = editor.Text;
        popup.IsOpen = false;
    }

    internal bool ContainsInteractionSource(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current == this || current == _spinPopup?.Child)
                return true;
        return false;
    }
}
