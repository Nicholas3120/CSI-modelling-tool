using System.Windows;
using CSIModellingTools.ViewModels;

namespace CSIModellingTools;

public partial class PileEccentricityCalculationSheetWindow : Window
{
    public PileEccentricityCalculationSheetWindow(PileEccentricityViewModel source)
    {
        InitializeComponent();
        DataContext = new PileEccentricityCalculationSheetViewModel(source);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
