using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeowField.App.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView() => InitializeComponent();

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.ToggleQueueCommand.CanExecute(null))
        {
            viewModel.ToggleQueueCommand.Execute(null);
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(QueueList, source) is ListBoxItem { Content: Domain.PlaylistItem item } &&
            viewModel.PlayQueueItemCommand.CanExecute(item))
        {
            viewModel.PlayQueueItemCommand.Execute(item);
        }
    }
}
