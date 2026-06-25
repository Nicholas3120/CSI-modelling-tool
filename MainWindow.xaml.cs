using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CSIModellingTools.ViewModels;

namespace CSIModellingTools;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ParametricModellingViewModel();
    }

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
