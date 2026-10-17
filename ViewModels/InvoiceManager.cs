using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

public partial class InvoiceManager : ObservableObject
{
    private readonly SalesManager sales;
    private readonly HashSet<RoomViewModel> observedCheckoutRooms = new();

    public ObservableCollection<InvoiceRecord> Invoices { get; } = new();
    public IReadOnlyList<string> DiscountTypes { get; } = new[] { "Số tiền", "Phần trăm" };

    [ObservableProperty] private RoomViewModel? checkoutRoom;
    [ObservableProperty] private InvoiceRecord? selectedInvoice;
    [ObservableProperty] private string surchargeInput = "0";
    [ObservableProperty] private string discountInput = "0";
    [ObservableProperty] private string discountType = "Số tiền";
    [ObservableProperty] private string roundingMinutesInput = "15";
    [ObservableProperty] private string message = string.Empty;
    public string ShopName { get; set; } = "Karaoke Đức Hiếu";
    public string ShopAddress { get; set; } = string.Empty;
    public string ShopPhone { get; set; } = string.Empty;
    public string ShopLogoPath { get; set; } = string.Empty;
    public string InvoiceGreeting { get; set; } = "Cảm ơn quý khách và hẹn gặp lại!";
    public string InvoiceFooter { get; set; } = string.Empty;
    public string InvoiceNumberFormat { get; set; } = "Theo thời gian";
    public bool PrinterConnected { get; set; }
    public decimal SingerHourlyRate { get; set; } = 100000;

    public bool IsCheckoutVisible => CheckoutRoom is not null;
    public IReadOnlyList<OrderLineSnapshot> CheckoutLines => CheckoutRoom is null ? Array.Empty<OrderLineSnapshot>() : sales.GetOrderSnapshot(CheckoutRoom);
    public decimal CheckoutServiceAmount => CheckoutLines.Sum(line => line.LineTotal) + CheckoutSingerAmount;
    public decimal CheckoutProductAmount => CheckoutLines.Sum(line => line.LineTotal);
    public decimal CheckoutSingerAmount => CheckoutRoom is null ? 0 : CalculateSingerCharge(CheckoutRoom, DateTime.Now, out _);
    public decimal CheckoutRoomAmount => CheckoutRoom is null ? 0 : CalculateRoomCharge(CheckoutRoom, DateTime.Now, out _);
    public int CheckoutBillableMinutes
    {
        get
        {
            if (CheckoutRoom is null) return 0;
            CalculateRoomCharge(CheckoutRoom, DateTime.Now, out var minutes);
            return minutes;
        }
    }
    public decimal CheckoutSubtotal => CheckoutRoomAmount + CheckoutServiceAmount;
    public decimal DiscountAmount => CalculateDiscount(DiscountInput, DiscountType, CheckoutSubtotal);
    public decimal CheckoutTotal => Math.Max(0, CheckoutSubtotal + ParseOptionalAmount(SurchargeInput) - DiscountAmount);

    public decimal GetRoomRunningTotal(RoomViewModel room)
    {
        var roomAmount = GetRoomTimeAmount(room);
        var singerAmount = GetSingerServiceAmount(room);
        var productAmount = GetRoomItemsAmount(room);
        var subtotal = roomAmount + singerAmount + productAmount;
        var discount = GetRoomDiscountAmount(room);
        return Math.Max(0, subtotal - discount);
    }

    public decimal GetRoomTimeAmount(RoomViewModel room) => room.Status == RoomStatus.InUse
        ? CalculateRoomCharge(room, DateTime.Now, out _) : 0;
    public decimal GetSingerServiceAmount(RoomViewModel room) => room.Status == RoomStatus.InUse
        ? CalculateSingerCharge(room, DateTime.Now, out _) : 0;
    public decimal GetRoomItemsAmount(RoomViewModel room) => sales.GetOrderSnapshot(room).Sum(line => line.LineTotal);
    public decimal GetRoomDiscountAmount(RoomViewModel room)
    {
        if (CheckoutRoom != room) return 0;
        return CalculateDiscount(DiscountInput, DiscountType,
            GetRoomTimeAmount(room) + GetSingerServiceAmount(room) + GetRoomItemsAmount(room));
    }
    public InvoiceManager(SalesManager sales)
    {
        this.sales = sales;
        sales.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SalesManager.TotalOpenOrderValue)) NotifyCheckout();
        };
    }

    public void BeginCheckout(RoomViewModel room)
    {
        if (room.Status != RoomStatus.InUse || room.StartedAt is null)
        {
            Message = "Phòng không có lượt sử dụng cần tính tiền.";
            return;
        }
        CheckoutRoom = room;
        SurchargeInput = "0";
        DiscountInput = "0";
        Message = string.Empty;
        NotifyCheckout();
    }

    [RelayCommand]
    private void CompleteCheckout()
    {
        if (CheckoutRoom is null || CheckoutRoom.Status != RoomStatus.InUse || CheckoutRoom.StartedAt is null)
        {
            Message = "Không tìm thấy lượt sử dụng đang hoạt động.";
            return;
        }
        if (!TryAmount(SurchargeInput, out var surcharge) || surcharge < 0)
        {
            Message = "Phụ thu phải là số không âm.";
            return;
        }
        if (!TryDiscount(DiscountInput, DiscountType, CheckoutSubtotal, out var discount))
        {
            Message = DiscountType == "Phần trăm"
                ? "Giảm giá phần trăm phải từ 0 đến 100."
                : "Giảm giá phải là số tiền không âm.";
            return;
        }
        if (!int.TryParse(RoundingMinutesInput, NumberStyles.None, CultureInfo.InvariantCulture, out var rounding) || rounding is < 1 or > 60)
        {
            Message = "Mức làm tròn phải từ 1 đến 60 phút.";
            return;
        }

        var room = CheckoutRoom;
        var endedAt = DateTime.Now;
        var roomCharge = CalculateRoomCharge(room, endedAt, out var billableMinutes);
        var lines = sales.GetOrderSnapshot(room);
        var singerAmount = CalculateSingerCharge(room, endedAt, out var singerService);
        var serviceTotal = lines.Sum(line => line.LineTotal) + singerAmount;
        var total = Math.Max(0, roomCharge + serviceTotal + surcharge - discount);
        var invoice = new InvoiceRecord(
            CreateInvoiceNumber(endedAt), room.Code, room.Name,
            room.StartedAt.Value, endedAt, billableMinutes, roomCharge, lines, surcharge, discount, total, singerService);

        Invoices.Insert(0, invoice);
        SelectedInvoice = invoice;
        sales.CompleteOrder(room);
        room.SingerCount = 0;
        room.SingerStartedAt = null;
        room.SingerEndedAt = null;
        room.EndSession();
        CheckoutRoom = null;
        Message = "Đã lập hóa đơn. Phòng đã chuyển sang trạng thái trống.";
        NotifyCheckout();
    }

    public void RecordCancelledRoom(RoomViewModel room)
    {
        if (room.Status != RoomStatus.InUse || room.StartedAt is null) return;
        var endedAt = DateTime.Now;
        var roomAmount = CalculateRoomCharge(room, endedAt, out var billableMinutes);
        var lines = sales.GetOrderSnapshot(room);
        var singerAmount = CalculateSingerCharge(room, endedAt, out var singerService);
        var total = roomAmount + lines.Sum(line => line.LineTotal) + singerAmount;
        var invoice = new InvoiceRecord(
            $"HUY{CreateInvoiceNumber(endedAt)[2..]}", room.Code, room.Name,
            room.StartedAt.Value, endedAt, billableMinutes, roomAmount, lines, 0, 0, total, singerService, true);
        Invoices.Insert(0, invoice);
        SelectedInvoice = invoice;
        Message = $"Đã lưu lượt {room.Name} vào lịch sử với trạng thái bị hủy bỏ.";
    }

    [RelayCommand]
    private void CancelCheckout()
    {
        CheckoutRoom = null;
        Message = string.Empty;
        NotifyCheckout();
    }

    [RelayCommand]
    private void SelectInvoice(InvoiceRecord? invoice)
    {
        if (invoice is not null) SelectedInvoice = invoice;
    }

    [RelayCommand]
    private void ExportInvoice(InvoiceRecord? invoice)
    {
        invoice ??= SelectedInvoice;
        if (invoice is null)
        {
            Message = "Chọn một hóa đơn để xuất.";
            return;
        }

        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KaraokeManager", "Invoices");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{invoice.Number}.html");
            File.WriteAllText(path, invoice.ToHtml(ShopName, ShopAddress, ShopPhone, InvoiceGreeting, InvoiceFooter, ShopLogoPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Message = $"Đã xuất: {path}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Message = $"Không thể xuất hóa đơn: {exception.Message}";
        }
    }

    [RelayCommand]
    private void PrintInvoice(InvoiceRecord? invoice)
    {
        invoice ??= SelectedInvoice;
        if (invoice is null) { Message = "Chọn một hóa đơn để in."; return; }
        if (invoice.IsCancelled) { Message = "Không thể in lượt đã bị hủy bỏ."; return; }
        var printer = PrinterService.Detect();
        if (!printer.IsConnected) { Message = printer.Message; return; }
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KaraokeManager", "Invoices");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{invoice.Number}.html");
            File.WriteAllText(path, invoice.ToHtml(ShopName, ShopAddress, ShopPhone, InvoiceGreeting, InvoiceFooter, ShopLogoPath), new UTF8Encoding(false));
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
                startInfo = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" };
            else
            {
                startInfo = new ProcessStartInfo("lp") { UseShellExecute = false };
                // The invoice is an HTML document. Without an explicit format, some
                // CUPS queues treat .html as plain text and print the markup itself.
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add("document-format=text/html");
                startInfo.ArgumentList.Add(path);
            }
            using var process = Process.Start(startInfo);
            if (process is null) { Message = "Không mở được lệnh in. Kiểm tra máy in mặc định."; return; }
            Message = OperatingSystem.IsWindows() ? "Đã gửi hóa đơn tới ứng dụng in mặc định." : "Đã gửi hóa đơn tới hàng đợi máy in.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Message = $"Không thể in hóa đơn: {exception.Message}";
        }
    }

    public void PrintInterimInvoice(RoomViewModel room)
    {
        if (room.Status != RoomStatus.InUse || room.StartedAt is null)
        {
            Message = "Phòng không có lượt sử dụng để in tạm tính.";
            return;
        }
        var endedAt = DateTime.Now;
        var roomAmount = CalculateRoomCharge(room, endedAt, out var minutes);
        var lines = sales.GetOrderSnapshot(room);
        var singerAmount = CalculateSingerCharge(room, endedAt, out var singer);
        var total = roomAmount + lines.Sum(line => line.LineTotal) + singerAmount;
        var interim = new InvoiceRecord($"TT{endedAt:yyyyMMddHHmmss}", room.Code, room.Name,
            room.StartedAt.Value, endedAt, minutes, roomAmount, lines, 0, 0, total, singer);
        PrintInvoice(interim);
        if (Message.StartsWith("Đã gửi", StringComparison.Ordinal)) Message = "Đã gửi hóa đơn tạm tính tới máy in.";
    }

    private string CreateInvoiceNumber(DateTime issuedAt) => InvoiceNumberFormat == "Theo ngày"
        ? $"HD{issuedAt:yyyyMMdd}-{Invoices.Count + 1:000}"
        : $"HD{issuedAt:yyyyMMddHHmmss}-{Invoices.Count + 1:000}";

    private decimal CalculateSingerCharge(RoomViewModel room, DateTime checkoutAt, out SingerServiceRecord? snapshot)
    {
        snapshot = null;
        if (room.SingerCount <= 0 || room.SingerStartedAt is null) return 0;
        var end = room.SingerEndedAt ?? checkoutAt;
        if (end <= room.SingerStartedAt.Value) return 0;
        var amount = decimal.Round((decimal)(end - room.SingerStartedAt.Value).TotalMinutes / 60m * SingerHourlyRate * room.SingerCount,
            0, MidpointRounding.AwayFromZero);
        snapshot = new SingerServiceRecord(room.SingerCount, room.SingerStartedAt.Value, end, SingerHourlyRate, amount);
        return amount;
    }

    private decimal CalculateRoomCharge(RoomViewModel room, DateTime endedAt, out int billableMinutes)
    {
        billableMinutes = 0;
        if (room.StartedAt is null || endedAt <= room.StartedAt.Value) return 0;
        var elapsedMinutes = (endedAt - room.StartedAt.Value).TotalMinutes;
        var rounding = int.TryParse(RoundingMinutesInput, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 1, 60) : 15;
        billableMinutes = Math.Max(rounding, (int)(Math.Ceiling(elapsedMinutes / rounding) * rounding));
        var billableEnd = room.StartedAt.Value.AddMinutes(billableMinutes);

        var peakMinutes = room.PeakHourlyRate.HasValue
            ? GetPeakMinutes(room.StartedAt.Value, billableEnd, room.PeakStart, room.PeakEnd)
            : 0;
        var normalMinutes = billableMinutes - peakMinutes;
        var amount = normalMinutes / 60m * room.HourlyRate
            + peakMinutes / 60m * (room.PeakHourlyRate ?? room.HourlyRate);
        return decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
    }

    private static int GetPeakMinutes(DateTime start, DateTime end, string peakStartText, string peakEndText)
    {
        if (!TimeSpan.TryParseExact(peakStartText, "hh\\:mm", CultureInfo.InvariantCulture, out var peakStart)
            || !TimeSpan.TryParseExact(peakEndText, "hh\\:mm", CultureInfo.InvariantCulture, out var peakEnd)
            || peakStart == peakEnd)
            return 0;

        var overlap = 0d;
        for (var date = start.Date.AddDays(-1); date <= end.Date; date = date.AddDays(1))
        {
            var windowStart = date.Add(peakStart);
            var windowEnd = date.Add(peakEnd);
            if (windowEnd <= windowStart) windowEnd = windowEnd.AddDays(1);
            var from = start > windowStart ? start : windowStart;
            var to = end < windowEnd ? end : windowEnd;
            if (to > from) overlap += (to - from).TotalMinutes;
        }
        return Math.Clamp((int)Math.Round(overlap), 0, (int)(end - start).TotalMinutes);
    }

    private void NotifyCheckout()
    {
        OnPropertyChanged(nameof(IsCheckoutVisible));
        OnPropertyChanged(nameof(CheckoutLines));
        OnPropertyChanged(nameof(CheckoutServiceAmount));
        OnPropertyChanged(nameof(CheckoutProductAmount));
        OnPropertyChanged(nameof(CheckoutSingerAmount));
        OnPropertyChanged(nameof(CheckoutRoomAmount));
        OnPropertyChanged(nameof(CheckoutBillableMinutes));
        OnPropertyChanged(nameof(CheckoutSubtotal));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(CheckoutTotal));
    }

    public void RefreshCheckout() => NotifyCheckout();

    private static decimal ParseOptionalAmount(string input) => TryAmount(input, out var value) ? value : 0;

    private static decimal CalculateDiscount(string input, string type, decimal subtotal) =>
        TryDiscount(input, type, subtotal, out var discount) ? discount : 0;

    private static bool TryDiscount(string input, string type, decimal subtotal, out decimal discount)
    {
        discount = 0;
        if (!TryAmount(input, out var value) || value < 0) return false;
        if (type == "Phần trăm")
        {
            if (value > 100) return false;
            discount = decimal.Round(subtotal * value / 100m, 0, MidpointRounding.AwayFromZero);
        }
        else discount = value;
        return true;
    }

    private static bool TryAmount(string input, out decimal value) => CurrencyParser.TryParse(input, out value);

    partial void OnCheckoutRoomChanged(RoomViewModel? value)
    {
        if (value is not null && observedCheckoutRooms.Add(value)) value.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(RoomViewModel.StartedAt) or nameof(RoomViewModel.SingerCount)
                or nameof(RoomViewModel.SingerStartedAt) or nameof(RoomViewModel.SingerEndedAt)) NotifyCheckout();
        };
        NotifyCheckout();
    }
    partial void OnSurchargeInputChanged(string value) { OnPropertyChanged(nameof(CheckoutTotal)); }
    partial void OnDiscountInputChanged(string value) { OnPropertyChanged(nameof(DiscountAmount)); OnPropertyChanged(nameof(CheckoutTotal)); }
    partial void OnDiscountTypeChanged(string value) { OnPropertyChanged(nameof(DiscountAmount)); OnPropertyChanged(nameof(CheckoutTotal)); }
    partial void OnRoundingMinutesInputChanged(string value)
    {
        OnPropertyChanged(nameof(CheckoutRoomAmount));
        OnPropertyChanged(nameof(CheckoutBillableMinutes));
        OnPropertyChanged(nameof(CheckoutSubtotal));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(CheckoutTotal));
    }
}

public sealed record InvoiceRecord(
    string Number,
    string RoomCode,
    string RoomName,
    DateTime StartedAt,
    DateTime EndedAt,
    int BillableMinutes,
    decimal RoomAmount,
    IReadOnlyList<OrderLineSnapshot> Lines,
    decimal Surcharge,
    decimal Discount,
    decimal Total,
    SingerServiceRecord? SingerService = null,
    bool IsCancelled = false)
{
    public decimal ServiceAmount => Lines.Sum(line => line.LineTotal) + (SingerService?.Amount ?? 0);
    public string IssuedAtText => EndedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("vi-VN"));
    public string TotalText => $"{Total:N0} ₫";
    public string StatusText => IsCancelled ? "Bị hủy bỏ" : "Đã hoàn tất";
    public bool CanPrint => !IsCancelled;

    public string ToPlainText(string shopName = "Karaoke Đức Hiếu", string address = "", string phone = "", string greeting = "Cảm ơn quý khách và hẹn gặp lại!", string footer = "")
    {
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        var text = new StringBuilder();
        text.AppendLine(shopName);
        if (!string.IsNullOrWhiteSpace(address)) text.AppendLine(address);
        if (!string.IsNullOrWhiteSpace(phone)) text.AppendLine($"Điện thoại: {phone}");
        text.AppendLine($"HÓA ĐƠN {Number}");
        if (IsCancelled) text.AppendLine("TRẠNG THÁI: BỊ HỦY BỎ");
        text.AppendLine($"Phòng: {RoomName} ({RoomCode})");
        text.AppendLine($"Bắt đầu: {StartedAt:dd/MM/yyyy HH:mm}");
        text.AppendLine($"Kết thúc: {EndedAt:dd/MM/yyyy HH:mm}");
        text.AppendLine($"Thời gian tính tiền: {BillableMinutes} phút");
        text.AppendLine($"Tiền phòng: {RoomAmount.ToString("N0", culture)} ₫");
        foreach (var line in Lines)
            text.AppendLine($"{line.Name} x {line.Quantity} {line.Unit}: {line.LineTotal.ToString("N0", culture)} ₫");
        if (SingerService is not null)
            text.AppendLine($"Nhân viên hát cùng x {SingerService.Count}: {SingerService.StartAt:dd/MM HH:mm}–{SingerService.EndAt:dd/MM HH:mm} · {SingerService.Amount.ToString("N0", culture)} ₫");
        text.AppendLine($"Tiền dịch vụ: {ServiceAmount.ToString("N0", culture)} ₫");
        text.AppendLine($"Phụ thu: {Surcharge.ToString("N0", culture)} ₫");
        text.AppendLine($"Giảm giá: {Discount.ToString("N0", culture)} ₫");
        text.AppendLine($"TỔNG CỘNG: {Total.ToString("N0", culture)} ₫");
        if (!string.IsNullOrWhiteSpace(greeting)) text.AppendLine(greeting);
        if (!string.IsNullOrWhiteSpace(footer)) text.AppendLine(footer);
        return text.ToString();
    }

    public string ToHtml(string shopName, string address, string phone, string greeting, string footer, string logoPath)
    {
        static string E(string value) => System.Net.WebUtility.HtmlEncode(value);
        var logo = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
            {
                var mime = Path.GetExtension(logoPath).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".bmp" => "image/bmp", _ => "" };
                if (mime.Length > 0) logo = $"<img class='logo' src='data:{mime};base64,{Convert.ToBase64String(System.IO.File.ReadAllBytes(logoPath))}' />";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        var lines = string.Join("", Lines.Select(line => $"<tr><td>{E(line.Name)} × {line.Quantity} {E(line.Unit)}</td><td>{line.LineTotal:N0} ₫</td></tr>"));
        return $"<!doctype html><html lang='vi'><meta charset='utf-8'><title>Hóa đơn {E(Number)}</title><style>body{{font:14px Arial,sans-serif;color:#222;max-width:560px;margin:32px auto;padding:20px}}header{{text-align:center;border-bottom:1px solid #ccc;padding-bottom:16px}}.logo{{max-width:120px;max-height:90px;object-fit:contain}}h1{{font-size:20px;margin:8px}}table{{width:100%;border-collapse:collapse;margin:16px 0}}td{{padding:7px 0;border-bottom:1px solid #eee}}td:last-child{{text-align:right;white-space:nowrap}}.total{{font-weight:bold;font-size:18px;border-top:2px solid #333;padding-top:10px;text-align:right}}footer{{text-align:center;margin-top:22px;color:#555;white-space:pre-line}}@media print{{body{{margin:0 auto}}}}</style><body><header>{logo}<h1>{E(shopName)}</h1><div>{E(address)}</div><div>{E(phone)}</div><p>HÓA ĐƠN {E(Number)}</p></header><p>Phòng: {E(RoomName)} ({E(RoomCode)})<br/>Thời gian: {StartedAt:dd/MM/yyyy HH:mm} – {EndedAt:dd/MM/yyyy HH:mm}</p><table><tr><td>Tiền phòng ({BillableMinutes} phút)</td><td>{RoomAmount:N0} ₫</td></tr>{lines}<tr><td>Dịch vụ hát cùng</td><td>{(SingerService?.Amount ?? 0):N0} ₫</td></tr><tr><td>Phụ thu</td><td>{Surcharge:N0} ₫</td></tr><tr><td>Giảm giá</td><td>−{Discount:N0} ₫</td></tr></table><div class='total'>TỔNG CỘNG: {Total:N0} ₫</div><footer>{E(greeting)}{(string.IsNullOrWhiteSpace(greeting) || string.IsNullOrWhiteSpace(footer) ? "" : "\n")}{E(footer)}</footer></body></html>";
    }
}

public sealed record SingerServiceRecord(int Count, DateTime StartAt, DateTime EndAt, decimal HourlyRate, decimal Amount);
