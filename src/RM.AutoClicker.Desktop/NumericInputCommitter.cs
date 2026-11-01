using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace RM.AutoClicker.Desktop;

internal static class NumericInputCommitter
{
    public static void CommitFocusedEditor(XamlRoot root)
    {
        if (FocusManager.GetFocusedElement(root) is not TextBox editor)
            return;

        for (DependencyObject? current = editor; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is NumberBox number)
            {
                number.Text = editor.Text;
                return;
            }
        }
    }
}
