using Avalonia.Controls;
using Avalonia.Interactivity;
using KaraokeManager.ViewModels;

namespace KaraokeManager.Views;

public partial class CheckoutSummaryWindow : Window
{
    public CheckoutSummaryWindow() => InitializeComponent();

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close(false);

    private void Confirm_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RoomDetailViewModel vm) return;
        vm.Main.CompleteCheckoutWithFallbackCommand.Execute(null);
        if (!vm.IsCheckoutVisible) Close(true);
    }
}
