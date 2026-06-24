using System.Windows;
using TrussModelling.ViewModels;

namespace TrussModelling;

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
