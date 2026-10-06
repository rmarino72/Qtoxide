using Avalonia.Controls;
using Qtoxide.ViewModels;

namespace Qtoxide.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Clears the "unread" marker added to the title by RequestAttention.
        Activated += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                Title = vm.Title;
        };
    }
}
