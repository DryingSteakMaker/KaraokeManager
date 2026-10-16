using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

public partial class RoomManagementManager : ObservableObject
{
    private readonly ObservableCollection<RoomViewModel> rooms;
    private readonly BookingManager bookings;
    private RoomViewModel? editingRoom;

    public ObservableCollection<string> RoomTypes { get; }
    public ObservableCollection<RoomViewModel> AvailableDestinations { get; } = new();

    [ObservableProperty] private RoomViewModel? selectedRoom;
    [ObservableProperty] private string roomCodeInput = string.Empty;
    [ObservableProperty] private string roomNameInput = string.Empty;
    [ObservableProperty] private string roomTypeInput = string.Empty;
    [ObservableProperty] private string capacityInput = "8";
    [ObservableProperty] private string hourlyRateInput = "110000";
    public decimal DefaultHourlyRate { get; set; } = 110000;
    [ObservableProperty] private string peakRateInput = string.Empty;
    [ObservableProperty] private string peakStartInput = "18:00";
    [ObservableProperty] private string peakEndInput = "23:00";
    [ObservableProperty] private string newRoomTypeInput = string.Empty;
    [ObservableProperty] private string? roomTypeToDelete;
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private bool isEditorVisible;
    [ObservableProperty] private RoomViewModel? sourceRoom;
    [ObservableProperty] private RoomViewModel? selectedDestination;
    [ObservableProperty] private bool isMoveFormVisible;
    [ObservableProperty] private string moveMessage = string.Empty;

    public string EditorTitle => editingRoom is null ? "Thêm phòng" : "Chỉnh sửa phòng";

    public RoomManagementManager(ObservableCollection<RoomViewModel> rooms, BookingManager bookings)
    {
        this.rooms = rooms;
        this.bookings = bookings;
        RoomTypes = new ObservableCollection<string>(rooms.Select(room => room.Type).Distinct().OrderBy(type => type));
        SelectedRoom = rooms.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(roomTypeInput)) RoomTypeInput = RoomTypes.FirstOrDefault() ?? "Tiêu chuẩn";

        foreach (var room in rooms)
            room.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RoomViewModel.Status)) RefreshDestinations();
            };
    }

    [RelayCommand]
    private void OpenCreate()
    {
        editingRoom = null;
        OnPropertyChanged(nameof(EditorTitle));
        RoomCodeInput = NextRoomCode();
        RoomNameInput = string.Empty;
        RoomTypeInput = RoomTypes.FirstOrDefault() ?? "Tiêu chuẩn";
        CapacityInput = "8";
        HourlyRateInput = DefaultHourlyRate.ToString(CultureInfo.InvariantCulture);
        PeakRateInput = string.Empty;
        PeakStartInput = "18:00";
        PeakEndInput = "23:00";
        Message = string.Empty;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void EditRoom(RoomViewModel? room)
    {
        if (room is null) return;
        editingRoom = room;
        SelectedRoom = room;
        OnPropertyChanged(nameof(EditorTitle));
        RoomCodeInput = room.Code;
        RoomNameInput = room.Name;
        RoomTypeInput = room.Type;
        CapacityInput = room.Capacity.ToString(CultureInfo.InvariantCulture);
        HourlyRateInput = room.HourlyRate.ToString(CultureInfo.InvariantCulture);
        PeakRateInput = room.PeakHourlyRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        PeakStartInput = room.PeakStart;
        PeakEndInput = room.PeakEnd;
        Message = string.Empty;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void SaveRoom()
    {
        var code = RoomCodeInput.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(RoomNameInput.Trim()))
        {
            Message = "Vui lòng nhập mã và tên phòng.";
            return;
        }
        if (editingRoom is null && rooms.Any(room => room.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
        {
            Message = "Mã phòng đã tồn tại.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RoomTypeInput) || !RoomTypes.Contains(RoomTypeInput))
        {
            Message = "Vui lòng chọn loại phòng hợp lệ.";
            return;
        }
        if (!int.TryParse(CapacityInput, NumberStyles.None, CultureInfo.InvariantCulture, out var capacity) || capacity < 1 || capacity > 100)
        {
            Message = "Sức chứa phải từ 1 đến 100 khách.";
            return;
        }
        if (!TryParsePrice(HourlyRateInput, out var hourlyRate) || hourlyRate <= 0)
        {
            Message = "Giá cơ bản phải là số lớn hơn 0.";
            return;
        }

        decimal? peakRate = null;
        if (!string.IsNullOrWhiteSpace(PeakRateInput))
        {
            if (!TryParsePrice(PeakRateInput, out var parsedPeak) || parsedPeak <= 0
                || !TimeSpan.TryParseExact(PeakStartInput, "hh\\:mm", CultureInfo.InvariantCulture, out var peakStart)
                || !TimeSpan.TryParseExact(PeakEndInput, "hh\\:mm", CultureInfo.InvariantCulture, out var peakEnd)
                || peakStart == peakEnd)
            {
                Message = "Giá cao điểm cần có giá hợp lệ và khung giờ HH:mm khác nhau.";
                return;
            }
            peakRate = parsedPeak;
        }

        if (editingRoom is null)
        {
            var room = new RoomViewModel(code, RoomNameInput.Trim(), RoomTypeInput, capacity, RoomStatus.Available,
                hourlyRate: hourlyRate, peakHourlyRate: peakRate, peakStart: PeakStartInput, peakEnd: PeakEndInput);
            rooms.Add(room);
            room.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RoomViewModel.Status)) RefreshDestinations();
            };
            SelectedRoom = room;
        }
        else
        {
            editingRoom.Name = RoomNameInput.Trim();
            editingRoom.Type = RoomTypeInput;
            editingRoom.Capacity = capacity;
            editingRoom.HourlyRate = hourlyRate;
            editingRoom.PeakHourlyRate = peakRate;
            editingRoom.PeakStart = PeakStartInput;
            editingRoom.PeakEnd = PeakEndInput;
        }

        Message = "Đã lưu thông tin phòng.";
        IsEditorVisible = false;
    }

    [RelayCommand]
    private void DeleteRoom(RoomViewModel? room)
    {
        if (room is null || !rooms.Contains(room)) return;
        if (room.Status == RoomStatus.InUse)
        {
            Message = "Không thể xóa phòng đang có khách.";
            return;
        }
        if (bookings.HasActiveBookingsForRoom(room.Code))
        {
            Message = "Không thể xóa phòng đang có đặt chỗ chưa kết thúc.";
            return;
        }
        rooms.Remove(room);
        SelectedRoom = rooms.FirstOrDefault();
        Message = $"Đã xóa {room.Name}.";
    }

    [RelayCommand]
    private void CloseEditor() => IsEditorVisible = false;

    [RelayCommand]
    private void AddRoomType()
    {
        var type = NewRoomTypeInput.Trim();
        if (type.Length == 0) return;
        if (RoomTypes.Any(existing => existing.Equals(type, StringComparison.OrdinalIgnoreCase)))
        {
            Message = "Loại phòng đã tồn tại.";
            return;
        }
        RoomTypes.Add(type);
        RoomTypeInput = type;
        NewRoomTypeInput = string.Empty;
        Message = "Đã thêm loại phòng.";
    }

    [RelayCommand]
    private void DeleteRoomType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        if (rooms.Any(room => room.Type == type))
        {
            Message = "Không thể xóa loại phòng đang được sử dụng.";
            return;
        }
        RoomTypes.Remove(type);
        if (RoomTypeInput == type) RoomTypeInput = RoomTypes.FirstOrDefault() ?? string.Empty;
        Message = "Đã xóa loại phòng.";
    }

    [RelayCommand]
    private void BeginMove(RoomViewModel? room)
    {
        if (room is null || room.Status != RoomStatus.InUse) return;
        SourceRoom = room;
        MoveMessage = string.Empty;
        RefreshDestinations();
        SelectedDestination = AvailableDestinations.FirstOrDefault();
        IsMoveFormVisible = true;
    }

    [RelayCommand]
    private void MoveSession()
    {
        if (SourceRoom is null || SourceRoom.Status != RoomStatus.InUse)
        {
            MoveMessage = "Phiên khách không còn hoạt động ở phòng nguồn.";
            return;
        }
        if (SelectedDestination is null || SelectedDestination.Status != RoomStatus.Available)
        {
            MoveMessage = "Vui lòng chọn phòng đích đang trống.";
            return;
        }
        if (bookings.HasReservationAtRoomNow(SelectedDestination.Code))
        {
            MoveMessage = "Phòng đích đang có đặt chỗ trong thời điểm hiện tại.";
            return;
        }

        SourceRoom.MoveSessionTo(SelectedDestination);
        IsMoveFormVisible = false;
        MoveMessage = string.Empty;
    }

    [RelayCommand]
    private void CloseMoveForm() => IsMoveFormVisible = false;

    private void RefreshDestinations()
    {
        var source = SourceRoom;
        AvailableDestinations.Clear();
        foreach (var room in rooms.Where(room => room.Status == RoomStatus.Available && room != source))
            AvailableDestinations.Add(room);
        if (SelectedDestination is null || !AvailableDestinations.Contains(SelectedDestination))
            SelectedDestination = AvailableDestinations.FirstOrDefault();
    }

    private string NextRoomCode()
    {
        var number = rooms.Select(room => room.Code)
            .Where(code => code.Length > 1 && code[0] == 'P' && int.TryParse(code[1..], out _))
            .Select(code => int.Parse(code[1..], CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0).Max() + 1;
        return $"P{number:00}";
    }

    private static bool TryParsePrice(string input, out decimal price) => CurrencyParser.TryParse(input, out price);
}
