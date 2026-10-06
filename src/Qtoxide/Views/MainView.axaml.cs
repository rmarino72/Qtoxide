using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Qtoxide.ViewModels;

namespace Qtoxide.Views;

public partial class MainView : UserControl
{
    private FriendViewModel? _watched;

    public MainView()
    {
        InitializeComponent();

        // Enter sends, Shift+Enter inserts a new line.
        Composer.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && DataContext is MainViewModel vm)
            {
                e.Handled = true;
                if (vm.SendCommand.CanExecute(null))
                    vm.SendCommand.Execute(null);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SelectedFriend))
                        Watch(vm.SelectedFriend);
                };
        };
    }

    /// <summary>Keeps the conversation scrolled to the newest message.</summary>
    private void Watch(FriendViewModel? friend)
    {
        if (_watched is not null)
            _watched.Messages.CollectionChanged -= OnMessagesChanged;
        _watched = friend;
        if (friend is not null)
            friend.Messages.CollectionChanged += OnMessagesChanged;
        ScrollToEnd();
        Composer.Focus();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
}
