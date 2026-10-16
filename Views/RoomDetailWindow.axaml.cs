using Avalonia.Controls;
using Avalonia.Input;
using KaraokeManager.ViewModels;
using System.Globalization;
using System;

namespace KaraokeManager.Views;

public partial class RoomDetailWindow : Window
{
    public RoomDetailWindow()
    {
        InitializeComponent();
    }

    private void Product_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if (sender is Control { DataContext: ProductRecord product } && DataContext is RoomDetailViewModel viewModel)
            viewModel.QuickAddProductCommand.Execute(product);
    }

    private async void OrderLine_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if (sender is not Control { DataContext: OrderLineRecord line } || DataContext is not RoomDetailViewModel vm) return;
        var quantity = await new OrderQuantityWindow(line.Name, line.Quantity).ShowDialog<int?>(this);
        if (quantity is > 0) vm.UpdateOrderLineQuantity(line, quantity.Value);
    }

    private async void OpenCheckoutSummary_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (DataContext is not RoomDetailViewModel vm) return;
        vm.Main.RefreshPrinterStatusCommand.Execute(null);
        vm.BeginCheckoutCommand.Execute(null);
        if (!vm.IsCheckoutVisible) return;
        var completed = await new CheckoutSummaryWindow { DataContext = vm }.ShowDialog<bool>(this);
        if (!completed) vm.Main.InvoiceManager.CancelCheckoutCommand.Execute(null);
    }

    private void PrintInterim_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (DataContext is RoomDetailViewModel vm) vm.PrintInterimInvoice();
    }

    private async void CancelRoom_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (DataContext is not RoomDetailViewModel vm) return;
        var confirm = await new RoomCancelConfirmationWindow(vm.Room.Name).ShowDialog<bool>(this);
        if (!confirm) return;
        vm.CancelRoomSession();
        Close();
    }

    private async void ChangeRoomStart_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (DataContext is not RoomDetailViewModel vm || vm.Room.StartedAt is null) return;
        var startedAt = vm.Room.StartedAt.Value;
        var result = await new RoomStartEditWindow(
            startedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            startedAt.ToString("HH:mm", CultureInfo.InvariantCulture)).ShowDialog<RoomStartEditResult?>(this);
        if (result is not null) vm.UpdateRoomStart(result.Date, result.Time);
    }
}
