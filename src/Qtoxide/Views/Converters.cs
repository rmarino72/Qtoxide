using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Qtoxide.ViewModels;
using Toxide;

namespace Qtoxide.Views;

public static class Converters
{
    public static readonly IValueConverter PresenceBrush = new FuncValueConverter<Presence, IBrush>(p => p switch
    {
        Presence.Online => new SolidColorBrush(Color.Parse("#3BA55C")),
        Presence.Away => new SolidColorBrush(Color.Parse("#FAA61A")),
        Presence.Busy => new SolidColorBrush(Color.Parse("#ED4245")),
        _ => new SolidColorBrush(Color.Parse("#80848E")),
    });

    public static readonly IValueConverter Alignment =
        new FuncValueConverter<bool, HorizontalAlignment>(outgoing => outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left);

    public static readonly IValueConverter StatusText = new FuncValueConverter<ToxUserStatus, string>(s => s switch
    {
        ToxUserStatus.Away => "Away",
        ToxUserStatus.Busy => "Busy",
        _ => "Available",
    });

    public static readonly IValueConverter DeliveredMark =
        new FuncValueConverter<bool, string>(delivered => delivered ? "✓✓" : "✓");
}
