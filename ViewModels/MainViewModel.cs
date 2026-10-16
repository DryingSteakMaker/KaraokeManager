using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<RoomViewModel> Rooms { get; } = new();

    public BookingManager BookingManager { get; }
    public ObservableCollection<ProductRecord> Products { get; } = new();
    public ObservableCollection<string> ProductCategories { get; } = new();
    public SalesManager SalesManager { get; }
    public InvoiceManager InvoiceManager { get; }
    public AccountManager AccountManager { get; }
    public LocalDatabase Database { get; }
    public ObservableCollection<BackupOption> Backups { get; } = new();
    public IReadOnlyList<string> ReportPeriods { get; } = new[] { "Hôm nay", "Tháng này", "Năm nay" };

    [ObservableProperty]
    private string selectedFilter = "Tất cả";

    [ObservableProperty]
    private string currentTime = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private string selectedSection = "Rooms";

    [ObservableProperty]
    private string productName = string.Empty;

    [ObservableProperty]
    private string productCategory = string.Empty;

    [ObservableProperty]
    private string productUnit = string.Empty;

    [ObservableProperty]
    private string productPrice = string.Empty;

    [ObservableProperty]
    private string productCost = string.Empty;

    [ObservableProperty]
    private string productMessage = string.Empty;

    [ObservableProperty]
    private string newCategoryName = string.Empty;

    [ObservableProperty]
    private string? selectedCategoryToDelete;

    [ObservableProperty] private string reportPeriod = "Tháng này";
    [ObservableProperty] private string defaultHourlyRateInput = "110000";
    [ObservableProperty] private string defaultRoundingInput = "15";
    [ObservableProperty] private string settingsMessage = string.Empty;
    [ObservableProperty] private string databaseMessage = string.Empty;
    [ObservableProperty] private BackupOption? selectedBackup;
    [ObservableProperty] private string singerCountInput = "1";
    [ObservableProperty] private string singerStartTimeInput = string.Empty;
    [ObservableProperty] private string singerEndTimeInput = string.Empty;
    [ObservableProperty] private string singerHourlyRateInput = "100000";
    [ObservableProperty] private string shopName = "Karaoke Đức Hiếu";
    [ObservableProperty] private string shopAddress = string.Empty;
    [ObservableProperty] private string shopPhone = string.Empty;
    [ObservableProperty] private string shopLogoPath = string.Empty;
    [ObservableProperty] private Bitmap? shopLogo;
    [ObservableProperty] private string invoiceGreeting = "Cảm ơn quý khách và hẹn gặp lại!";
    [ObservableProperty] private string invoiceFooter = string.Empty;
    [ObservableProperty] private string invoiceNumberFormat = "Theo thời gian";
    [ObservableProperty] private string accentColor = "Xanh lá";
    [ObservableProperty] private string themeMode = "Sáng";
    [ObservableProperty] private string fontSizeOption = "Vừa";
    public IReadOnlyList<string> AccentColors { get; } = new[] { "Xanh lá", "Xanh dương", "Tím", "Cam" };
    public IReadOnlyList<string> ThemeModes { get; } = new[] { "Sáng", "Tối", "Theo hệ thống" };
    public IReadOnlyList<string> FontSizeOptions { get; } = new[] { "Nhỏ", "Vừa", "Lớn" };
    public IReadOnlyList<string> InvoiceNumberFormats { get; } = new[] { "Theo thời gian", "Theo ngày" };
    [ObservableProperty] private PrinterStatus printerStatus = new(false, "", "", "Chưa phát hiện máy in mặc định.");
    public bool IsPrinterConnected => PrinterStatus.IsConnected;
    public bool CanPrintSelectedInvoice => IsPrinterConnected && InvoiceManager.SelectedInvoice?.CanPrint == true;
    public string PrinterStatusMessage => PrinterStatus.DisplayText;

    private ProductRecord? editingProduct;
    private readonly HashSet<BookingRecord> observedBookings = new();
    private readonly HashSet<RoomViewModel> observedRooms = new();
    private bool isRestoringDatabase;

    public bool IsRoomsSection => SelectedSection == "Rooms";
    public bool IsBookingsSection => SelectedSection == "Bookings";
    public bool IsProductsSection => SelectedSection == "Products";
    public bool IsRoomManagementSection => SelectedSection == "RoomManagement";
    public bool IsInvoicesSection => SelectedSection == "Invoices";
    public bool IsSettingsSection => SelectedSection == "Settings";
    public bool IsReportsSection => SelectedSection == "Reports";
    [ObservableProperty] private RoomViewModel? selectedRoomDetail;
    [ObservableProperty] private bool isRoomConfirmationVisible;
    [ObservableProperty] private string roomConfirmationText = string.Empty;
    [ObservableProperty] private string roomStartTimeInput = string.Empty;
    public bool IsAuthenticated => AccountManager.IsAuthenticated;
    public bool IsLoginVisible => !AccountManager.IsAuthenticated;
    public RoomManagementManager RoomManagement { get; }
    public string ProductFormTitle => editingProduct is null ? "Thêm sản phẩm" : "Sửa sản phẩm";
    public string ProductSaveButtonText => editingProduct is null ? "Thêm sản phẩm" : "Lưu thay đổi";
    public string CurrentUserDisplay => AccountManager.CurrentUser ?? "";

    public string TodayLabel => DateTime.Now.ToString("dddd, dd 'tháng' MM, yyyy", new System.Globalization.CultureInfo("vi-VN"));

    public IEnumerable<RoomViewModel> FilteredRooms => SelectedFilter switch
    {
        "Trống" => Rooms.Where(room => room.Status == RoomStatus.Available),
        "Đang sử dụng" => Rooms.Where(room => room.Status == RoomStatus.InUse),
        "Đã đặt" => Rooms.Where(room => room.Status == RoomStatus.Reserved),
        _ => Rooms,
    };

    public int AvailableCount => Rooms.Count(room => room.Status == RoomStatus.Available);
    public int InUseCount => Rooms.Count(room => room.Status == RoomStatus.InUse);
    public int ReservedCount => Rooms.Count(room => room.Status == RoomStatus.Reserved);
    public int TotalRooms => Rooms.Count;
    public string TodayRevenue => $"{(InvoiceManager?.Invoices.Where(invoice => !invoice.IsCancelled && invoice.EndedAt.Date == DateTime.Today).Sum(invoice => invoice.Total) ?? 0):N0} ₫";
    public string ActiveRevenue => $"{(SalesManager?.TotalOpenOrderValue ?? 0):N0} ₫";
    public int ProductCount => Products.Count;
    public int CategoryCount => ProductCategories.Count;
    private DateTime ReportStart => ReportPeriod switch { "Hôm nay" => DateTime.Today, "Năm nay" => new DateTime(DateTime.Today.Year, 1, 1), _ => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) };
    private DateTime ReportEnd => ReportPeriod switch { "Hôm nay" => DateTime.Today.AddDays(1), "Năm nay" => new DateTime(DateTime.Today.Year + 1, 1, 1), _ => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1) };
    public string ReportRevenue => $"{ReportInvoices.Sum(i => i.Total):N0} ₫";
    public string ReportRoomRevenue => $"{ReportInvoices.Sum(i => i.RoomAmount):N0} ₫";
    public string ReportServiceRevenue => $"{ReportInvoices.Sum(i => i.ServiceAmount):N0} ₫";
    public string ReportProfit => $"{ReportInvoices.Sum(i => i.Total - i.Lines.Sum(line => line.UnitCostPrice * line.Quantity)):N0} ₫";
    public IEnumerable<InvoiceRecord> ReportInvoices => InvoiceManager is null ? Array.Empty<InvoiceRecord>() : InvoiceManager.Invoices.Where(i => !i.IsCancelled && i.EndedAt >= ReportStart && i.EndedAt < ReportEnd).ToList();
    public IEnumerable<ReportRow> RevenueByRoom => ReportInvoices.GroupBy(i => i.RoomName).Select(g => new ReportRow(g.Key, $"{g.Sum(i => i.Total):N0} ₫", g.Sum(i => i.Total))).OrderByDescending(row => row.AmountNumeric);
    public IEnumerable<ReportRow> BestSellers => ReportInvoices.SelectMany(i => i.Lines).GroupBy(line => line.Name).Select(g => new ReportRow(g.Key, $"{g.Sum(line => line.Quantity)} món · {g.Sum(line => line.LineTotal):N0} ₫", g.Sum(line => line.Quantity))).OrderByDescending(row => row.AmountNumeric).Take(5);
    public IEnumerable<ReportRow> MostUsedRooms => ReportInvoices.GroupBy(i => i.RoomName).Select(g => new ReportRow(g.Key, $"{g.Sum(i => i.BillableMinutes):N0} phút", g.Sum(i => i.BillableMinutes))).OrderByDescending(row => row.AmountNumeric);

    public MainViewModel()
    {
        Database = new LocalDatabase();
        DatabaseMessage = Database.Message;
        LoadUserPreferences(Database.State);
        RefreshBackups();
        var migratedLegacyRoomStatus = false;
        if (Database.State.Rooms.Count > 0)
        {
            foreach (var room in Database.State.Rooms)
            {
                var wasLegacyCleaningStatus = (int)room.Status == 3;
                var status = wasLegacyCleaningStatus ? RoomStatus.Available : room.Status;
                migratedLegacyRoomStatus |= wasLegacyCleaningStatus;
                Rooms.Add(new RoomViewModel(room.Code, room.Name, room.Type, room.Capacity, status,
                    wasLegacyCleaningStatus ? null : room.StartedAt,
                    room.HourlyRate, room.PeakHourlyRate, room.PeakStart, room.PeakEnd)
                {
                    SingerCount = wasLegacyCleaningStatus ? 0 : room.SingerCount,
                    SingerStartedAt = wasLegacyCleaningStatus ? null : room.SingerStartedAt,
                    SingerEndedAt = wasLegacyCleaningStatus ? null : room.SingerEndedAt,
                });
            }
        }
        else
        {
            foreach (var room in new[] { new RoomViewModel("P01", "Phòng 01", "Tiêu chuẩn", 8, RoomStatus.Available),
                new RoomViewModel("P02", "Phòng 02", "Tiêu chuẩn", 8, RoomStatus.Available), new RoomViewModel("P03", "Phòng VIP 01", "VIP", 12, RoomStatus.Available) }) Rooms.Add(room);
        }
        foreach (var category in Database.State.Categories.Where(category =>
                     !category.Equals("Snack", StringComparison.OrdinalIgnoreCase)
                     && !category.Equals("Dịch vụ", StringComparison.OrdinalIgnoreCase))) ProductCategories.Add(category);
        if (ProductCategories.Count == 0)
        {
            ProductCategories.Add("Đồ uống");
            ProductCategories.Add("Đồ ăn");
        }
        foreach (var product in Database.State.Products) Products.Add(product);
        DefaultHourlyRateInput = Database.State.DefaultHourlyRate.ToString(CultureInfo.InvariantCulture);
        DefaultRoundingInput = Database.State.RoundingMinutes.ToString(CultureInfo.InvariantCulture);
        SingerHourlyRateInput = Database.State.SingerHourlyRate.ToString(CultureInfo.InvariantCulture);
        BookingManager = new BookingManager(Rooms);
        BookingManager.LoadBookings(Database.State.Bookings);
        RoomManagement = new RoomManagementManager(Rooms, BookingManager);
        RoomManagement.DefaultHourlyRate = Database.State.DefaultHourlyRate;
        SalesManager = new SalesManager(Products, Rooms);
        InvoiceManager = new InvoiceManager(SalesManager);
        ApplyUserPreferences();
        InvoiceManager.PrinterConnected = IsPrinterConnected;
        foreach (var invoice in Database.State.Invoices.OrderByDescending(i => i.EndedAt)) InvoiceManager.Invoices.Add(invoice);
        InvoiceManager.SelectedInvoice = InvoiceManager.Invoices.FirstOrDefault();
        SalesManager.LoadOpenOrders(Database.State.OpenOrders);
        InvoiceManager.RoundingMinutesInput = DefaultRoundingInput;
        InvoiceManager.SingerHourlyRate = Database.State.SingerHourlyRate;
        AccountManager = new AccountManager();
        AccountManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AccountManager.IsAuthenticated) or nameof(AccountManager.CurrentUser))
            {
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(IsLoginVisible));
                OnPropertyChanged(nameof(CurrentUserDisplay));
            }
        };
        InvoiceManager.Invoices.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TodayRevenue));
            OnPropertyChanged(nameof(ActiveRevenue));
            RefreshReport();
            PersistDatabase();
        };
        InvoiceManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InvoiceManager.SelectedInvoice)) OnPropertyChanged(nameof(CanPrintSelectedInvoice));
        };
        SalesManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SalesManager.TotalOpenOrderValue))
            {
                OnPropertyChanged(nameof(ActiveRevenue));
                PersistDatabase();
            }
        };
        Rooms.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
                foreach (RoomViewModel room in args.NewItems) ObserveRoom(room);
            RefreshRoomSummary();
            PersistDatabase();
        };
        Products.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ProductCount));
            OnPropertyChanged(nameof(CategoryCount));
            PersistDatabase();
        };
        ProductCategories.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(CategoryCount)); PersistDatabase(); };
        RefreshPrinterStatus();
        foreach (var room in Rooms) ObserveRoom(room);
        BookingManager.Bookings.CollectionChanged += (_, _) => { ObserveBookings(); PersistDatabase(); RefreshReport(); };
        ObserveBookings();
        if (migratedLegacyRoomStatus) PersistDatabase();

        var clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        clock.Tick += (_, _) =>
        {
            CurrentTime = DateTime.Now.ToString("HH:mm");
            OnPropertyChanged(nameof(TodayLabel));
            foreach (var room in Rooms) room.RefreshElapsed();
            if (InvoiceManager.IsCheckoutVisible) InvoiceManager.RefreshCheckout();
        };
        clock.Start();
    }

    partial void OnSelectedFilterChanged(string value) => OnPropertyChanged(nameof(FilteredRooms));

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsRoomsSection));
        OnPropertyChanged(nameof(IsBookingsSection));
        OnPropertyChanged(nameof(IsProductsSection));
        OnPropertyChanged(nameof(IsRoomManagementSection));
        OnPropertyChanged(nameof(IsInvoicesSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(IsReportsSection));
    }

    [RelayCommand]
    private void Navigate(string? section)
    {
        if (section is "Rooms" or "Bookings" or "Products" or "RoomManagement" or "Invoices" or "Settings" or "Reports")
        {
            SelectedSection = section;
        }
        if (section == "Invoices") RefreshPrinterStatus();
    }

    [RelayCommand]
    private void OpenRoomDetail(RoomViewModel? room)
    {
        if (room is null) return;
        if (InvoiceManager.CheckoutRoom is not null && InvoiceManager.CheckoutRoom != room)
            InvoiceManager.CancelCheckoutCommand.Execute(null);
        SelectedRoomDetail = room;
        RoomStartTimeInput = room.StartedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        SalesManager.SelectRoom(room);
        IsRoomConfirmationVisible = room.Status is RoomStatus.Available or RoomStatus.InUse;
        RoomConfirmationText = room.Status switch
        {
            RoomStatus.Available => $"Mở {room.Name} và bắt đầu tính giờ?",
            RoomStatus.InUse => $"Mở phần tính tiền cho {room.Name}?",
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private void ConfirmRoomStatus()
    {
        var room = SelectedRoomDetail;
        if (room is null) return;
        switch (room.Status)
        {
            case RoomStatus.Available:
                if (BookingManager.HasReservationAtRoomNow(room.Code))
                {
                    RoomConfirmationText = "Phòng đang có lịch đặt trong thời điểm này.";
                    return;
                }
                room.StartSession();
                break;
            case RoomStatus.InUse:
                InvoiceManager.BeginCheckout(room);
                break;
        }
        IsRoomConfirmationVisible = false;
        RefreshRoomSummary();
    }

    [RelayCommand]
    private void OpenDetailConfirmation()
    {
        var room = SelectedRoomDetail;
        if (room is null) return;
        RoomConfirmationText = room.Status switch
        {
            RoomStatus.Available => $"Mở {room.Name} và bắt đầu tính giờ?",
            RoomStatus.InUse => $"Mở phần tính tiền cho {room.Name}?",
            _ => "Phòng đã có lịch đặt.",
        };
        IsRoomConfirmationVisible = true;
    }

    [RelayCommand]
    private void DismissRoomConfirmation() => IsRoomConfirmationVisible = false;

    [RelayCommand]
    private void SaveRoomStartTime()
    {
        if (SelectedRoomDetail is null || SelectedRoomDetail.Status != RoomStatus.InUse) return;
        if (!DateTime.TryParseExact(RoomStartTimeInput, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startedAt)
            || startedAt > DateTime.Now)
        {
            InvoiceManager.Message = "Thời gian bắt đầu không hợp lệ hoặc ở tương lai.";
            return;
        }
        SelectedRoomDetail.StartedAt = DateTime.SpecifyKind(startedAt, DateTimeKind.Local);
        InvoiceManager.Message = "Đã cập nhật thời gian bắt đầu.";
    }

    [RelayCommand]
    private void BeginDetailMove()
    {
        if (SelectedRoomDetail is not null) RoomManagement.BeginMoveCommand.Execute(SelectedRoomDetail);
    }

    [RelayCommand]
    private void CompleteDetailMove()
    {
        var source = SelectedRoomDetail;
        var destination = RoomManagement.SelectedDestination;
        RoomManagement.MoveSessionCommand.Execute(null);
        if (destination?.Status == RoomStatus.InUse)
        {
            if (source is not null) SalesManager.TransferOrder(source, destination);
            SelectedRoomDetail = destination;
            SalesManager.SelectRoom(destination);
            RoomStartTimeInput = destination.StartedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    [RelayCommand]
    private void CloseRoomDetail()
    {
        if (SelectedRoomDetail is not null && InvoiceManager.CheckoutRoom == SelectedRoomDetail)
            InvoiceManager.CancelCheckoutCommand.Execute(null);
        IsRoomConfirmationVisible = false;
        SelectedRoomDetail = null;
    }

    [RelayCommand]
    private void QuickAddProduct(ProductRecord? product)
    {
        if (product is null || SelectedRoomDetail is null || SelectedRoomDetail.Status != RoomStatus.InUse) return;
        SalesManager.SelectRoom(SelectedRoomDetail);
        SalesManager.SelectedProduct = product;
        SalesManager.QuantityInput = "1";
        SalesManager.AddOrderLineCommand.Execute(null);
    }

    [RelayCommand]
    private void SaveSingerService()
    {
        var room = SelectedRoomDetail;
        if (room is null || room.Status != RoomStatus.InUse) return;
        if (!int.TryParse(SingerCountInput, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0 || count > 20)
        {
            InvoiceManager.Message = "Số nhân viên hát cùng phải từ 0 đến 20.";
            return;
        }
        if (count == 0)
        {
            room.SingerCount = 0;
            room.SingerStartedAt = null;
            room.SingerEndedAt = null;
            InvoiceManager.Message = "Đã xóa dịch vụ nhân viên hát cùng.";
            return;
        }
        if (!DateTime.TryParseExact(SingerStartTimeInput, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var starts))
        {
            InvoiceManager.Message = "Thời gian bắt đầu dịch vụ không hợp lệ.";
            return;
        }
        DateTime? ends = null;
        if (!string.IsNullOrWhiteSpace(SingerEndTimeInput))
        {
            if (!DateTime.TryParseExact(SingerEndTimeInput, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endValue) || endValue <= starts)
            {
                InvoiceManager.Message = "Thời gian kết thúc phải sau thời gian bắt đầu.";
                return;
            }
            ends = DateTime.SpecifyKind(endValue, DateTimeKind.Local);
        }
        room.SingerCount = count;
        room.SingerStartedAt = DateTime.SpecifyKind(starts, DateTimeKind.Local);
        room.SingerEndedAt = ends;
        InvoiceManager.Message = "Đã lưu dịch vụ nhân viên hát cùng.";
    }

    [RelayCommand]
    private void EndSingerServiceNow()
    {
        SingerEndTimeInput = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        SaveSingerService();
    }

    partial void OnSelectedRoomDetailChanged(RoomViewModel? value)
    {
        SingerCountInput = value?.SingerCount.ToString(CultureInfo.InvariantCulture) ?? "1";
        SingerStartTimeInput = value?.SingerStartedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            ?? DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        SingerEndTimeInput = value?.SingerEndedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    [RelayCommand]
    private void BeginBooking()
    {
        SelectedSection = "Bookings";
        BookingManager.OpenFormCommand.Execute(null);
    }

    [RelayCommand]
    private void BeginRoomMove(RoomViewModel? room)
    {
        if (room is null) return;
        SelectedSection = "RoomManagement";
        RoomManagement.BeginMoveCommand.Execute(room);
    }

    [RelayCommand]
    private void CompleteCheckout()
    {
        var room = InvoiceManager.CheckoutRoom;
        if (room is null) return;
        InvoiceManager.CompleteCheckoutCommand.Execute(null);
        if (room.Status != RoomStatus.InUse)
        {
            SelectedSection = "Invoices";
            RefreshRoomSummary();
        }
    }

    [RelayCommand]
    private void CompleteCheckoutAndPrint()
    {
        RefreshPrinterStatus();
        if (!IsPrinterConnected)
        {
            InvoiceManager.Message = PrinterStatusMessage;
            return;
        }
        var currentCount = InvoiceManager.Invoices.Count;
        CompleteCheckout();
        if (InvoiceManager.Invoices.Count > currentCount && InvoiceManager.SelectedInvoice is not null)
            InvoiceManager.PrintInvoiceCommand.Execute(InvoiceManager.SelectedInvoice);
    }

    [RelayCommand]
    private void RefreshPrinterStatus()
    {
        PrinterStatus = PrinterService.Detect();
        InvoiceManager.PrinterConnected = PrinterStatus.IsConnected;
        OnPropertyChanged(nameof(IsPrinterConnected));
        OnPropertyChanged(nameof(CanPrintSelectedInvoice));
        OnPropertyChanged(nameof(PrinterStatusMessage));
    }

    [RelayCommand]
    private void CompleteCheckoutAndExport()
    {
        var currentCount = InvoiceManager.Invoices.Count;
        CompleteCheckout();
        if (InvoiceManager.Invoices.Count > currentCount && InvoiceManager.SelectedInvoice is not null)
            InvoiceManager.ExportInvoiceCommand.Execute(InvoiceManager.SelectedInvoice);
    }

    [RelayCommand]
    private void CompleteCheckoutWithFallback()
    {
        RefreshPrinterStatus();
        var currentCount = InvoiceManager.Invoices.Count;
        CompleteCheckout();
        if (InvoiceManager.Invoices.Count <= currentCount || InvoiceManager.SelectedInvoice is null) return;
        if (IsPrinterConnected)
        {
            InvoiceManager.PrintInvoiceCommand.Execute(InvoiceManager.SelectedInvoice);
            if (InvoiceManager.Message.StartsWith("Không thể in", StringComparison.OrdinalIgnoreCase)
                || InvoiceManager.Message.StartsWith("Không mở", StringComparison.OrdinalIgnoreCase)
                || InvoiceManager.Message.StartsWith("Chưa phát hiện", StringComparison.OrdinalIgnoreCase))
                InvoiceManager.ExportInvoiceCommand.Execute(InvoiceManager.SelectedInvoice);
        }
        else InvoiceManager.ExportInvoiceCommand.Execute(InvoiceManager.SelectedInvoice);
    }

    public void PrintInterimInvoice(RoomViewModel room)
    {
        RefreshPrinterStatus();
        if (!IsPrinterConnected)
        {
            InvoiceManager.Message = PrinterStatusMessage;
            return;
        }
        InvoiceManager.PrintInterimInvoice(room);
    }

    public void CancelRoomSession(RoomViewModel room)
    {
        if (room.Status != RoomStatus.InUse) return;
        InvoiceManager.RecordCancelledRoom(room);
        SalesManager.CompleteOrder(room);
        room.SingerCount = 0;
        room.SingerStartedAt = null;
        room.SingerEndedAt = null;
        room.EndSession();
        InvoiceManager.Message = $"Đã lưu lượt sử dụng của {room.Name} vào lịch sử với trạng thái bị hủy bỏ.";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedSection = "Settings";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (!CurrencyParser.TryParse(DefaultHourlyRateInput, out var defaultRate) || defaultRate <= 0)
        {
            SettingsMessage = "Giá phòng mặc định phải lớn hơn 0.";
            return;
        }
        if (!int.TryParse(DefaultRoundingInput, NumberStyles.None, CultureInfo.InvariantCulture, out var rounding) || rounding is < 1 or > 60)
        {
            SettingsMessage = "Thời gian làm tròn phải từ 1 đến 60 phút.";
            return;
        }
        if (!CurrencyParser.TryParse(SingerHourlyRateInput, out var singerRate) || singerRate < 0)
        {
            SettingsMessage = "Giá nhân viên hát cùng phải là số không âm.";
            return;
        }
        foreach (var room in Rooms) room.HourlyRate = defaultRate;
        RoomManagement.DefaultHourlyRate = defaultRate;
        InvoiceManager.RoundingMinutesInput = rounding.ToString(CultureInfo.InvariantCulture);
        InvoiceManager.SingerHourlyRate = singerRate;
        ApplyUserPreferences();
        SettingsMessage = "Đã lưu tất cả cài đặt.";
        PersistDatabase();
    }

    private void LoadUserPreferences(DatabaseState state)
    {
        ShopName = state.ShopName; ShopAddress = state.ShopAddress; ShopPhone = state.ShopPhone; ShopLogoPath = state.ShopLogoPath;
        InvoiceGreeting = state.InvoiceGreeting; InvoiceFooter = state.InvoiceFooter;
        InvoiceNumberFormat = InvoiceNumberFormats.Contains(state.InvoiceNumberFormat) ? state.InvoiceNumberFormat : "Theo thời gian";
        AccentColor = AccentColors.Contains(state.AccentColor) ? state.AccentColor : "Xanh lá";
        ThemeMode = ThemeModes.Contains(state.ThemeMode) ? state.ThemeMode : "Sáng";
        FontSizeOption = FontSizeOptions.Contains(state.FontSizeOption) ? state.FontSizeOption : "Vừa";
        LoadShopLogo();
    }

    private void ApplyUserPreferences()
    {
        ShopName = string.IsNullOrWhiteSpace(ShopName) ? "Quán karaoke" : ShopName.Trim();
        InvoiceManager.ShopName = ShopName;
        InvoiceManager.ShopAddress = ShopAddress.Trim();
        InvoiceManager.ShopPhone = ShopPhone.Trim();
        InvoiceManager.ShopLogoPath = ShopLogoPath;
        InvoiceManager.InvoiceGreeting = InvoiceGreeting.Trim();
        InvoiceManager.InvoiceFooter = InvoiceFooter.Trim();
        InvoiceManager.InvoiceNumberFormat = InvoiceNumberFormat;
        ApplyThemeResources();
        LoadShopLogo();
    }

    private void ApplyThemeResources()
    {
        if (Application.Current is not { } app) return;
        var dark = ThemeMode == "Tối";
        var accent = AccentColor switch { "Xanh dương" => "#2563EB", "Tím" => "#7C3AED", "Cam" => "#D97706", _ => dark ? "#86A897" : "#263B35" };
        var accentColor = Color.Parse(accent);
        app.Resources["AccentBrush"] = new SolidColorBrush(accentColor);
        var tintColor = dark
            ? Color.FromArgb(255, (byte)(accentColor.R * .24), (byte)(accentColor.G * .24), (byte)(accentColor.B * .24))
            : Color.FromArgb(255, (byte)(accentColor.R * .13 + 255 * .87), (byte)(accentColor.G * .13 + 255 * .87), (byte)(accentColor.B * .13 + 255 * .87));
        app.Resources["AccentTintBrush"] = new SolidColorBrush(tintColor);
        app.Resources["CanvasBrush"] = new SolidColorBrush(Color.Parse(dark ? "#17191C" : "#F5F6F8"));
        app.Resources["SurfaceBrush"] = new SolidColorBrush(Color.Parse(dark ? "#24272B" : "#FFFFFF"));
        app.Resources["NavigationBrush"] = new SolidColorBrush(tintColor);
        app.Resources["NavigationTextBrush"] = new SolidColorBrush(Color.Parse(dark ? "#F0F1F2" : "#252A31"));
        app.Resources["NavigationMutedBrush"] = new SolidColorBrush(Color.Parse(dark ? "#D2D7D2" : "#4F5A54"));
        app.Resources["TextBrush"] = new SolidColorBrush(Color.Parse(dark ? "#F0F1F2" : "#252A31"));
        app.Resources["MutedBrush"] = new SolidColorBrush(Color.Parse(dark ? "#C1C5C9" : "#555D66"));
        app.Resources["OutlineBrush"] = new SolidColorBrush(Color.Parse(dark ? "#41464C" : "#E8EAED"));
        app.RequestedThemeVariant = ThemeMode switch { "Tối" => Avalonia.Styling.ThemeVariant.Dark, "Sáng" => Avalonia.Styling.ThemeVariant.Light, _ => Avalonia.Styling.ThemeVariant.Default };
    }

    private void LoadShopLogo()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ShopLogoPath) && System.IO.File.Exists(ShopLogoPath))
            {
                ShopLogo = new Bitmap(ShopLogoPath);
                return;
            }

            var defaultLogo = AssetLoader.Open(new Uri("avares://KaraokeManager/Assets/logo_default_128.png"));
            ShopLogo = new Bitmap(defaultLogo);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.InvalidOperationException)
        {
            ShopLogo = null;
        }
    }

    public void SetShopLogoPath(string path) { ShopLogoPath = path; LoadShopLogo(); if (InvoiceManager is not null) InvoiceManager.ShopLogoPath = path; PersistDatabase(); }

    partial void OnThemeModeChanged(string value)
    {
        ApplyThemeResources();
        if (Database is not null) PersistDatabase();
    }

    partial void OnAccentColorChanged(string value)
    {
        ApplyThemeResources();
        if (Database is not null) PersistDatabase();
    }

    partial void OnFontSizeOptionChanged(string value)
    {
        OnPropertyChanged(nameof(InterfaceFontSize));
        if (Database is not null) PersistDatabase();
    }
    public double InterfaceFontSize => FontSizeOption switch { "Nhỏ" => 12, "Lớn" => 16, _ => 14 };
    public bool HasShopLogo => ShopLogo is not null;
    partial void OnShopLogoChanged(Bitmap? value) => OnPropertyChanged(nameof(HasShopLogo));

    private void ObserveBookings()
    {
        foreach (var booking in BookingManager.Bookings)
            if (observedBookings.Add(booking)) booking.PropertyChanged += (_, _) => { PersistDatabase(); RefreshReport(); };
    }

    private void ObserveRoom(RoomViewModel room)
    {
        if (observedRooms.Add(room)) room.PropertyChanged += (_, _) => { RefreshRoomSummary(); PersistDatabase(); };
    }

    private void RefreshReport()
    {
        OnPropertyChanged(nameof(ReportRevenue));
        OnPropertyChanged(nameof(ReportRoomRevenue));
        OnPropertyChanged(nameof(ReportServiceRevenue));
        OnPropertyChanged(nameof(ReportProfit));
        OnPropertyChanged(nameof(RevenueByRoom));
        OnPropertyChanged(nameof(BestSellers));
        OnPropertyChanged(nameof(MostUsedRooms));
    }

    private void PersistDatabase()
    {
        if (isRestoringDatabase || Database is null || BookingManager is null || InvoiceManager is null || SalesManager is null) return;
        var state = new DatabaseState
        {
            Rooms = Rooms.Select(room => new RoomData(room.Code, room.Name, room.Type, room.Capacity, room.Status,
                room.StartedAt, room.HourlyRate, room.PeakHourlyRate, room.PeakStart, room.PeakEnd,
                room.SingerCount, room.SingerStartedAt, room.SingerEndedAt)).ToList(),
            Bookings = BookingManager.Bookings.Select(b => new BookingData(b.RoomCode, b.RoomName, b.GuestName, b.GuestPhone, b.StartAt, b.EndAt, b.IsCancelled)).ToList(),
            Products = Products.ToList(), Categories = ProductCategories.ToList(), Invoices = InvoiceManager.Invoices.ToList(),
            OpenOrders = SalesManager.SnapshotOpenOrders().ToList(),
            RoundingMinutes = int.TryParse(InvoiceManager.RoundingMinutesInput, out var rounding) ? Math.Clamp(rounding, 1, 60) : 15,
            DefaultHourlyRate = CurrencyParser.TryParse(DefaultHourlyRateInput, out var rate) ? rate : 110000,
            SingerHourlyRate = CurrencyParser.TryParse(SingerHourlyRateInput, out var singerRate) ? singerRate : 100000,
            ShopName = ShopName.Trim(), ShopAddress = ShopAddress.Trim(), ShopPhone = ShopPhone.Trim(), ShopLogoPath = ShopLogoPath,
            InvoiceGreeting = InvoiceGreeting.Trim(), InvoiceFooter = InvoiceFooter.Trim(), InvoiceNumberFormat = InvoiceNumberFormat,
            AccentColor = AccentColor, ThemeMode = ThemeMode, FontSizeOption = FontSizeOption,
        };
        Database.Save(state);
        if (DatabaseMessage != Database.Message) DatabaseMessage = Database.Message;
        RefreshBackups();
    }

    [RelayCommand]
    private void CreateBackupNow()
    {
        if (Database.CreateBackup()) RefreshBackups();
        DatabaseMessage = Database.Message;
    }

    [RelayCommand]
    private void RestoreSelectedBackup()
    {
        if (SelectedBackup is null)
        {
            DatabaseMessage = "Hãy chọn một bản sao lưu để khôi phục.";
            return;
        }
        var state = Database.RestoreBackup(SelectedBackup.Path);
        if (state is null) { DatabaseMessage = Database.Message; return; }

        isRestoringDatabase = true;
        try
        {
            Rooms.Clear();
            foreach (var room in state.Rooms)
                Rooms.Add(new RoomViewModel(room.Code, room.Name, room.Type, room.Capacity, room.Status,
                    room.StartedAt, room.HourlyRate, room.PeakHourlyRate, room.PeakStart, room.PeakEnd)
                {
                    SingerCount = room.SingerCount,
                    SingerStartedAt = room.SingerStartedAt,
                    SingerEndedAt = room.SingerEndedAt,
                });
            BookingManager.Bookings.Clear();
            BookingManager.LoadBookings(state.Bookings);
            Products.Clear();
            foreach (var product in state.Products) Products.Add(product);
            ProductCategories.Clear();
            foreach (var category in state.Categories) ProductCategories.Add(category);
            InvoiceManager.Invoices.Clear();
            foreach (var invoice in state.Invoices.OrderByDescending(i => i.EndedAt)) InvoiceManager.Invoices.Add(invoice);
            InvoiceManager.SelectedInvoice = InvoiceManager.Invoices.FirstOrDefault();
            SalesManager.ClearOpenOrders();
            SalesManager.LoadOpenOrders(state.OpenOrders);
            DefaultHourlyRateInput = state.DefaultHourlyRate.ToString(CultureInfo.InvariantCulture);
            DefaultRoundingInput = state.RoundingMinutes.ToString(CultureInfo.InvariantCulture);
            SingerHourlyRateInput = state.SingerHourlyRate.ToString(CultureInfo.InvariantCulture);
            LoadUserPreferences(state);
            ApplyUserPreferences();
            RoomManagement.DefaultHourlyRate = state.DefaultHourlyRate;
            InvoiceManager.RoundingMinutesInput = DefaultRoundingInput;
            InvoiceManager.SingerHourlyRate = state.SingerHourlyRate;
            OnPropertyChanged(nameof(TotalRooms));
            RefreshRoomSummary();
            RefreshReport();
        }
        finally { isRestoringDatabase = false; }
        DatabaseMessage = Database.Message;
        RefreshBackups();
    }

    private void RefreshBackups()
    {
        if (Database is null) return;
        var current = SelectedBackup?.Path;
        Backups.Clear();
        foreach (var path in Database.GetBackups())
            Backups.Add(new BackupOption(System.IO.Path.GetFileNameWithoutExtension(path), path));
        SelectedBackup = Backups.FirstOrDefault(item => item.Path == current) ?? Backups.FirstOrDefault();
    }

    partial void OnReportPeriodChanged(string value) => RefreshReport();

    [RelayCommand]
    private void SelectFilter(string? filter)
    {
        if (!string.IsNullOrWhiteSpace(filter)) SelectedFilter = filter;
    }

    [RelayCommand]
    private void ToggleRoom(RoomViewModel? room)
    {
        if (room is null) return;
        if (room.Status == RoomStatus.Available)
            room.StartSession();
        else if (room.Status == RoomStatus.InUse)
        {
            InvoiceManager.BeginCheckout(room);
            SelectedSection = "Invoices";
        }
        RefreshRoomSummary();
    }

    [RelayCommand]
    private void RefreshClock() => CurrentTime = DateTime.Now.ToString("HH:mm");

    [RelayCommand]
    private void AddProduct()
    {
        var name = ProductName.Trim();
        var category = ProductCategory.Trim();
        var unit = ProductUnit.Trim();

        if (name.Length == 0 || category.Length == 0 || unit.Length == 0)
        {
            ProductMessage = "Vui lòng nhập tên sản phẩm, danh mục và đơn vị.";
            return;
        }
        if (IsRemovedCategory(category))
        {
            ProductMessage = "Danh mục Snack và Dịch vụ đã được bỏ. Hãy chọn danh mục khác.";
            return;
        }

        if (!CurrencyParser.TryParse(ProductPrice, out var salePrice)
            || salePrice <= 0)
        {
            ProductMessage = "Giá bán phải là số lớn hơn 0.";
            return;
        }

        if (!CurrencyParser.TryParse(ProductCost, out var costPrice)
            || costPrice < 0)
        {
            ProductMessage = "Giá vốn phải là số không âm.";
            return;
        }

        if (!ProductCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
            ProductCategories.Add(category);

        if (editingProduct is null)
        {
            var nextCode = Products.Select(product => int.TryParse(product.Code.AsSpan(2), out var number) ? number : 0)
                .DefaultIfEmpty(0).Max() + 1;
            Products.Add(new ProductRecord($"SP{nextCode:000}", name, category, unit, salePrice, costPrice));
        }
        else
        {
            var index = Products.IndexOf(editingProduct);
            if (index >= 0)
                Products[index] = new ProductRecord(editingProduct.Code, name, category, unit, salePrice, costPrice);
            editingProduct = null;
            OnPropertyChanged(nameof(ProductFormTitle));
            OnPropertyChanged(nameof(ProductSaveButtonText));
        }
        ProductName = string.Empty;
        ProductCategory = string.Empty;
        ProductUnit = string.Empty;
        ProductPrice = string.Empty;
        ProductCost = string.Empty;
        ProductMessage = string.Empty;
    }

    [RelayCommand]
    private void EditProduct(ProductRecord? product)
    {
        if (product is null) return;
        editingProduct = product;
        ProductName = product.Name;
        ProductCategory = product.Category;
        ProductUnit = product.Unit;
        ProductPrice = product.SalePrice.ToString(CultureInfo.InvariantCulture);
        ProductCost = product.CostPrice.ToString(CultureInfo.InvariantCulture);
        ProductMessage = string.Empty;
        OnPropertyChanged(nameof(ProductFormTitle));
        OnPropertyChanged(nameof(ProductSaveButtonText));
    }

    [RelayCommand]
    private void DeleteProduct(ProductRecord? product)
    {
        if (product is null) return;
        if (SalesManager.IsProductInOpenOrder(product.Code))
        {
            ProductMessage = "Không thể xóa sản phẩm đang nằm trong đơn món của phòng.";
            return;
        }
        Products.Remove(product);
        ProductMessage = $"Đã xóa {product.Name}.";
    }

    [RelayCommand]
    private void AddProductCategory()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0) return;
        if (IsRemovedCategory(name))
        {
            ProductMessage = "Danh mục Snack và Dịch vụ đã được bỏ.";
            return;
        }
        if (ProductCategories.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ProductMessage = "Danh mục đã tồn tại.";
            return;
        }
        ProductCategories.Add(name);
        ProductCategory = name;
        NewCategoryName = string.Empty;
        ProductMessage = "Đã thêm danh mục.";
    }

    private static bool IsRemovedCategory(string category) =>
        category.Equals("Snack", StringComparison.OrdinalIgnoreCase)
        || category.Equals("Dịch vụ", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void DeleteProductCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        if (Products.Any(product => product.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            ProductMessage = "Không thể xóa danh mục đang có sản phẩm.";
            return;
        }
        ProductCategories.Remove(category);
        if (ProductCategory.Equals(category, StringComparison.OrdinalIgnoreCase)) ProductCategory = string.Empty;
        ProductMessage = "Đã xóa danh mục.";
    }

    [RelayCommand]
    private void CancelCheckout() => InvoiceManager.CancelCheckoutCommand.Execute(null);

    private void RefreshRoomSummary()
    {
        OnPropertyChanged(nameof(FilteredRooms));
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(InUseCount));
        OnPropertyChanged(nameof(ReservedCount));
    }
}

public enum RoomStatus { Available, InUse, Reserved }
public sealed record ReportRow(string Name, string Amount, decimal AmountNumeric);
public sealed record BackupOption(string Label, string Path);

public partial class RoomViewModel : ObservableObject
{
    public string Code { get; }
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string type;

    [ObservableProperty]
    private int capacity;

    [ObservableProperty]
    private decimal hourlyRate;

    [ObservableProperty]
    private decimal? peakHourlyRate;

    [ObservableProperty]
    private string peakStart = "18:00";

    [ObservableProperty]
    private string peakEnd = "23:00";

    [ObservableProperty]
    private RoomStatus status;

    [ObservableProperty]
    private DateTime? startedAt;

    [ObservableProperty] private int singerCount;
    [ObservableProperty] private DateTime? singerStartedAt;
    [ObservableProperty] private DateTime? singerEndedAt;

    public RoomViewModel(string code, string name, string type, int capacity, RoomStatus status, DateTime? startedAt = null,
        decimal? hourlyRate = null, decimal? peakHourlyRate = null, string peakStart = "18:00", string peakEnd = "23:00")
    {
        Code = code;
        this.name = name;
        this.type = type;
        this.capacity = capacity;
        this.status = status;
        this.startedAt = startedAt;
        this.hourlyRate = hourlyRate ?? (type == "VIP" ? 180000 : type == "Gia đình" ? 140000 : 110000);
        this.peakHourlyRate = peakHourlyRate;
        this.peakStart = peakStart;
        this.peakEnd = peakEnd;
    }

    public string HourlyRateText => $"{HourlyRate:N0} ₫/giờ";
    public string PeakRateText => PeakHourlyRate is null ? "Chưa cài giá giờ cao điểm" : $"{PeakStart}–{PeakEnd}: {PeakHourlyRate.Value:N0} ₫/giờ";

    public string StatusText => Status switch
    {
        RoomStatus.InUse => "Đang sử dụng",
        RoomStatus.Reserved => "Đã đặt",
        _ => "Trống",
    };

    public string ElapsedText => StartedAt is null ? "Sẵn sàng đón khách" : $"Bắt đầu lúc {StartedAt.Value:HH:mm} · {Math.Max(1, (int)(DateTime.Now - StartedAt.Value).TotalMinutes)} phút";
    public string ActionText => Status switch
    {
        RoomStatus.InUse => "Tính tiền",
        RoomStatus.Available => "Mở phòng",
        RoomStatus.Reserved => "Xem đặt phòng",
        _ => "Mở phòng",
    };

    public IBrush StatusColor => new SolidColorBrush(Color.Parse(Status switch
    {
        RoomStatus.InUse => "#D97745",
        RoomStatus.Reserved => "#7A68C7",
        _ => "#329B78",
    }));

    public bool CanToggle => Status is RoomStatus.Available or RoomStatus.InUse;
    public bool CanTransfer => Status == RoomStatus.InUse;

    public void StartSession()
    {
        StartedAt = DateTime.Now;
        Status = RoomStatus.InUse;
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CanToggle));
    }

    public void EndSession()
    {
        StartedAt = null;
        Status = RoomStatus.Available;
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CanToggle));
    }

    public void RefreshElapsed() => OnPropertyChanged(nameof(ElapsedText));

    public void MoveSessionTo(RoomViewModel destination)
    {
        var originalStart = StartedAt;
        var originalSingerCount = SingerCount;
        var originalSingerStart = SingerStartedAt;
        var originalSingerEnd = SingerEndedAt;
        StartedAt = null;
        SingerCount = 0;
        SingerStartedAt = null;
        SingerEndedAt = null;
        Status = RoomStatus.Available;
        destination.StartedAt = originalStart;
        destination.SingerCount = originalSingerCount;
        destination.SingerStartedAt = originalSingerStart;
        destination.SingerEndedAt = originalSingerEnd;
        destination.Status = RoomStatus.InUse;
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CanToggle));
    }

    public void ReceiveSession(DateTime? startedAt)
    {
        StartedAt = startedAt;
        Status = RoomStatus.InUse;
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CanToggle));
    }

    partial void OnStatusChanged(RoomStatus value)
    {
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CanToggle)); OnPropertyChanged(nameof(CanTransfer));
    }

    partial void OnHourlyRateChanged(decimal value) => OnPropertyChanged(nameof(HourlyRateText));
    partial void OnPeakHourlyRateChanged(decimal? value) => OnPropertyChanged(nameof(PeakRateText));
    partial void OnPeakStartChanged(string value) => OnPropertyChanged(nameof(PeakRateText));
    partial void OnPeakEndChanged(string value) => OnPropertyChanged(nameof(PeakRateText));

    partial void OnStartedAtChanged(DateTime? value) => OnPropertyChanged(nameof(ElapsedText));
}
