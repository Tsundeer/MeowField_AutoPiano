using System.Windows.Controls;
using System.Windows.Input;

namespace MeowField.App.Views;

public partial class OnlineLibraryView : UserControl
{
    public OnlineLibraryView() => InitializeComponent();

    private void OnTracksDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OnlineLibraryViewModel viewModel && viewModel.LoadSelectedCommand.CanExecute(null))
        {
            viewModel.LoadSelectedCommand.Execute(null);
        }
    }
}
