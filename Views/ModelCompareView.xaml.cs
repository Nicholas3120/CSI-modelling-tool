using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CSIModellingTools.Views;

public partial class ModelCompareView : UserControl
{
    public ModelCompareView()
    {
        InitializeComponent();
    }

    // Mirrors the window-level handler so the extracted Model Compare view stays self-contained: pressing Enter
    // in a text box commits the binding and advances focus.
    private void TextBox_ConfirmOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
            return;

        BindingExpression? binding = textBox.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();
        textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }
}
