using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

/// <summary>Window-scoped state for one room while sharing the application's persisted managers.</summary>
public partial class RoomDetailViewModel : ObservableObject
{
    public MainViewModel Main { get; }
    public RoomViewModel Room { get; }
    public ObservableCollection<OrderLineRecord> RoomOrderLines { get; }
    public ObservableCollection<ProductRecord> Products => Main.Products;
    public System.Collections.Generic.IEnumerable<ProductRecord> FilteredProducts => Products.Where(product =>
        string.IsNullOrWhiteSpace(ProductSearchText)
        || product.Name.Contains(ProductSearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
        || product.Category.Contains(ProductSearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
    public System.Collections.Generic.IEnumerable<OrderLineRecord> FoodAndDrinkOrderLines => RoomOrderLines.Where(line =>
    {
        var category = Products.FirstOrDefault(product => product.Code == line.ProductCode)?.Category;
        return category is not null && (category.Equals("Đồ ăn", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Đồ uống", StringComparison.OrdinalIgnoreCase));
    }).ToList();
    public bool IsPrinterConnected => Main.IsPrinterConnected;
    public string CheckoutConfirmText => IsPrinterConnected ? "Xác nhận và in hóa đơn" : "Xác nhận và xuất .txt";
    public decimal RoomOrderTotal => RoomOrderLines.Sum(line => line.LineTotal);
    public decimal RoomTimeAmount => Main.InvoiceManager.GetRoomTimeAmount(Room);
    public decimal RoomSingerAmount => Main.InvoiceManager.GetSingerServiceAmount(Room);
    public decimal RoomItemsAmount => Main.InvoiceManager.GetRoomItemsAmount(Room);
    public decimal RoomDiscountAmount => Main.InvoiceManager.GetRoomDiscountAmount(Room);
    public decimal RoomTotal => Main.InvoiceManager.GetRoomRunningTotal(Room);
    public bool IsCheckoutVisible => Main.InvoiceManager.CheckoutRoom == Room;
    public bool IsRoomActionPanelVisible => Room.CanTransfer && !IsCheckoutVisible && !IsMoveFormVisible;
    public bool IsSingerServiceActive => Room.SingerCount > 0 && Room.SingerStartedAt is not null;
    public bool IsSingerServiceInactive => !IsSingerServiceActive;
    public bool CanAddSingerService => IsSingerServiceInactive && !IsSingerEditorVisible;
    public string SingerElapsedText
    {
        get
        {
            if (!IsSingerServiceActive) return string.Empty;
            var end = Room.SingerEndedAt ?? DateTime.Now;
            var minutes = Math.Max(0, (int)(end - Room.SingerStartedAt!.Value).TotalMinutes);
            return $"Đã hát {(minutes / 60)} giờ {minutes % 60} phút";
        }
    }
    public decimal SingerHourlyRate => Main.InvoiceManager.SingerHourlyRate;
    public ObservableCollection<RoomViewModel> AvailableDestinations { get; } = new();

    [ObservableProperty] private bool isRoomConfirmationVisible;
    [ObservableProperty] private string roomConfirmationText = string.Empty;
    [ObservableProperty] private string singerCountInput = "1";
    [ObservableProperty] private string singerStartTimeInput = string.Empty;
    [ObservableProperty] private string singerStartDateInput = string.Empty;
    [ObservableProperty] private RoomViewModel? selectedDestination;
    [ObservableProperty] private bool isMoveFormVisible;
    [ObservableProperty] private string moveMessage = string.Empty;
    [ObservableProperty] private string productSearchText = string.Empty;
    [ObservableProperty] private bool isSingerEditorVisible;

    public RoomDetailViewModel(MainViewModel main, RoomViewModel room)
    {
        Main = main;
        Room = room;
        RoomOrderLines = main.SalesManager.GetOrderLinesForRoom(room);
        SingerCountInput = room.SingerCount.ToString(CultureInfo.InvariantCulture);
        SingerStartTimeInput = room.SingerStartedAt?.ToString("HH:mm", CultureInfo.InvariantCulture)
            ?? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        SingerStartDateInput = room.SingerStartedAt?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            ?? DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        RoomOrderLines.CollectionChanged += OnOrderLinesChanged;
        Main.Products.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredProducts));
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsPrinterConnected)) { OnPropertyChanged(nameof(IsPrinterConnected)); OnPropertyChanged(nameof(CheckoutConfirmText)); }
        };
        foreach (var line in RoomOrderLines) line.PropertyChanged += OnOrderLineChanged;
        room.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsCheckoutVisible));
            OnPropertyChanged(nameof(IsRoomActionPanelVisible));
            OnPropertyChanged(nameof(IsSingerServiceActive));
            OnPropertyChanged(nameof(IsSingerServiceInactive));
            OnPropertyChanged(nameof(CanAddSingerService));
            OnPropertyChanged(nameof(SingerElapsedText));
            NotifyRoomTotals();
        };
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentTime)) { OnPropertyChanged(nameof(SingerElapsedText)); NotifyRoomTotals(); }
        };
        main.InvoiceManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(InvoiceManager.CheckoutRoom) or nameof(InvoiceManager.SingerHourlyRate))
            {
                OnPropertyChanged(nameof(IsCheckoutVisible));
                OnPropertyChanged(nameof(IsRoomActionPanelVisible));
                OnPropertyChanged(nameof(SingerHourlyRate));
                NotifyRoomTotals();
            }
            else if (args.PropertyName is nameof(InvoiceManager.DiscountInput) or nameof(InvoiceManager.DiscountType))
                NotifyRoomTotals();
        };
        IsRoomConfirmationVisible = room.Status is RoomStatus.Available or RoomStatus.InUse;
        SetConfirmationText();
    }

    [RelayCommand]
    private void OpenRoomStatusConfirmation()
    {
        SetConfirmationText();
        IsRoomConfirmationVisible = true;
    }

    [RelayCommand]
    private void ConfirmRoomStatus()
    {
        switch (Room.Status)
        {
            case RoomStatus.Available:
                if (Main.BookingManager.HasReservationAtRoomNow(Room.Code))
                {
                    RoomConfirmationText = "Phòng đang có lịch đặt trong thời điểm này.";
                    return;
                }
                Room.StartSession();
                break;
            case RoomStatus.InUse:
                Main.InvoiceManager.BeginCheckout(Room);
                break;
        }
        IsRoomConfirmationVisible = false;
    }

    [RelayCommand]
    private void DismissRoomConfirmation() => IsRoomConfirmationVisible = false;

    public bool UpdateRoomStart(string dateInput, string timeInput)
    {
        if (Room.Status != RoomStatus.InUse || Room.StartedAt is null) return false;
        if (!DateOnly.TryParseExact(dateInput, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            Main.InvoiceManager.Message = "Ngày bắt đầu không hợp lệ.";
            return false;
        }
        if (!TimeOnly.TryParseExact(timeInput, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
        {
            Main.InvoiceManager.Message = "Giờ bắt đầu không hợp lệ.";
            return false;
        }
        var startedAt = date.ToDateTime(time);
        if (startedAt > DateTime.Now)
        {
            Main.InvoiceManager.Message = "Thời gian bắt đầu không được ở tương lai.";
            return false;
        }
        Room.StartedAt = DateTime.SpecifyKind(startedAt, DateTimeKind.Local);
        Main.InvoiceManager.Message = "Đã cập nhật thời gian bắt đầu.";
        return true;
    }

    public void UpdateOrderLineQuantity(OrderLineRecord line, int quantity)
    {
        Main.SalesManager.UpdateOrderLineQuantityForRoom(Room, line, quantity);
        Main.InvoiceManager.RefreshCheckout();
        NotifyRoomTotals();
    }

    public void PrintInterimInvoice() => Main.PrintInterimInvoice(Room);

    public void CancelRoomSession() => Main.CancelRoomSession(Room);

    [RelayCommand]
    private void QuickAddProduct(ProductRecord? product)
    {
        if (product is null) return;
        Main.SalesManager.AddOrderLineForRoom(Room, product);
    }

    [RelayCommand]
    private void RemoveOrderLine(OrderLineRecord? line) => Main.SalesManager.RemoveOrderLineForRoom(Room, line);

    [RelayCommand]
    private void SaveSingerService()
    {
        if (Room.Status != RoomStatus.InUse) return;
        if (!int.TryParse(SingerCountInput, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0 || count > 20)
        {
            Main.InvoiceManager.Message = "Số nhân viên hát cùng phải từ 0 đến 20.";
            return;
        }
        if (count == 0)
        {
            Room.SingerCount = 0;
            Room.SingerStartedAt = null;
            Room.SingerEndedAt = null;
            IsSingerEditorVisible = false;
            Main.InvoiceManager.Message = "Đã xóa dịch vụ nhân viên hát cùng.";
            return;
        }
        if (!TimeOnly.TryParseExact(SingerStartTimeInput, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
        {
            Main.InvoiceManager.Message = "Thời gian bắt đầu dịch vụ không hợp lệ.";
            return;
        }
        if (!DateOnly.TryParseExact(SingerStartDateInput, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var serviceDate))
        {
            Main.InvoiceManager.Message = "Ngày bắt đầu dịch vụ không hợp lệ.";
            return;
        }
        var date = serviceDate.ToDateTime(TimeOnly.MinValue);
        var starts = date.Add(time.ToTimeSpan());
        if (starts > DateTime.Now)
        {
            Main.InvoiceManager.Message = "Thời gian bắt đầu không được ở tương lai.";
            return;
        }
        Room.SingerCount = count;
        Room.SingerStartedAt = DateTime.SpecifyKind(starts, DateTimeKind.Local);
        Room.SingerEndedAt = null;
        IsSingerEditorVisible = false;
        Main.InvoiceManager.Message = "Đã lưu dịch vụ nhân viên hát cùng.";
    }

    [RelayCommand]
    private void AddSingerService()
    {
        SingerCountInput = Room.SingerCount > 0 ? Room.SingerCount.ToString(CultureInfo.InvariantCulture) : "1";
        SingerStartTimeInput = Room.SingerStartedAt?.ToString("HH:mm", CultureInfo.InvariantCulture)
            ?? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        SingerStartDateInput = Room.SingerStartedAt?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            ?? DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        IsSingerEditorVisible = true;
    }

    [RelayCommand]
    private void CancelSingerEditor() => IsSingerEditorVisible = false;

    partial void OnIsSingerEditorVisibleChanged(bool value) => OnPropertyChanged(nameof(CanAddSingerService));

    [RelayCommand]
    private void BeginMove()
    {
        if (Room.Status != RoomStatus.InUse) return;
        AvailableDestinations.Clear();
        foreach (var destination in Main.Rooms.Where(candidate => candidate != Room && candidate.Status == RoomStatus.Available))
            AvailableDestinations.Add(destination);
        SelectedDestination = AvailableDestinations.FirstOrDefault();
        MoveMessage = string.Empty;
        IsMoveFormVisible = true;
    }

    [RelayCommand]
    private void CompleteMove()
    {
        if (SelectedDestination is null || SelectedDestination.Status != RoomStatus.Available)
        {
            MoveMessage = "Vui lòng chọn phòng đích đang trống.";
            return;
        }
        if (Main.BookingManager.HasReservationAtRoomNow(SelectedDestination.Code))
        {
            MoveMessage = "Phòng đích đang có đặt chỗ trong thời điểm hiện tại.";
            return;
        }
        var destination = SelectedDestination;
        Main.RoomManagement.BeginMoveCommand.Execute(Room);
        Main.RoomManagement.SelectedDestination = destination;
        Main.RoomManagement.MoveSessionCommand.Execute(null);
        if (destination.Status == RoomStatus.InUse)
            Main.SalesManager.TransferOrder(Room, destination);
        IsMoveFormVisible = false;
        MoveMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelMove() => IsMoveFormVisible = false;

    [RelayCommand]
    private void CompleteCheckout() => Main.CompleteCheckoutCommand.Execute(null);

    [RelayCommand]
    private void BeginCheckout()
    {
        if (Room.Status == RoomStatus.InUse) Main.InvoiceManager.BeginCheckout(Room);
    }

    [RelayCommand]
    private void CompleteCheckoutAndPrint() => Main.CompleteCheckoutAndPrintCommand.Execute(null);

    [RelayCommand]
    private void CompleteCheckoutAndExport() => Main.CompleteCheckoutAndExportCommand.Execute(null);

    [RelayCommand]
    private void Close()
    {
        if (Main.InvoiceManager.CheckoutRoom == Room)
            Main.InvoiceManager.CancelCheckoutCommand.Execute(null);
    }

    partial void OnProductSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredProducts));

    partial void OnIsMoveFormVisibleChanged(bool value) => OnPropertyChanged(nameof(IsRoomActionPanelVisible));

    private void SetConfirmationText() => RoomConfirmationText = Room.Status switch
    {
        RoomStatus.Available => $"Mở {Room.Name} và bắt đầu tính giờ?",
        RoomStatus.InUse => $"Mở phần tính tiền cho {Room.Name}?",
        _ => string.Empty,
    };

    private void OnOrderLinesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (OrderLineRecord line in args.NewItems) line.PropertyChanged += OnOrderLineChanged;
        OnPropertyChanged(nameof(RoomOrderTotal));
        OnPropertyChanged(nameof(FoodAndDrinkOrderLines));
        Main.InvoiceManager.RefreshCheckout();
        NotifyRoomTotals();
    }

    private void OnOrderLineChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OrderLineRecord.Quantity) or nameof(OrderLineRecord.LineTotal))
        {
            OnPropertyChanged(nameof(RoomOrderTotal));
            Main.InvoiceManager.RefreshCheckout();
            NotifyRoomTotals();
        }
    }

    private void NotifyRoomTotals()
    {
        OnPropertyChanged(nameof(RoomTimeAmount));
        OnPropertyChanged(nameof(RoomSingerAmount));
        OnPropertyChanged(nameof(RoomItemsAmount));
        OnPropertyChanged(nameof(RoomDiscountAmount));
        OnPropertyChanged(nameof(RoomTotal));
    }
}
