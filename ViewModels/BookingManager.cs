using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

public partial class BookingManager : ObservableObject
{
    private readonly ObservableCollection<RoomViewModel> rooms;

    public ObservableCollection<BookingRecord> Bookings { get; } = new();
    public ObservableCollection<RoomViewModel> Rooms { get; }
    public ObservableCollection<RoomViewModel> AvailableRooms { get; } = new();

    [ObservableProperty]
    private RoomViewModel? selectedRoom;

    [ObservableProperty]
    private string guestName = string.Empty;

    [ObservableProperty]
    private string guestPhone = string.Empty;

    [ObservableProperty]
    private string startTime = DateTime.Now.AddHours(1).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string durationMinutes = "120";

    [ObservableProperty]
    private string validationMessage = string.Empty;

    [ObservableProperty]
    private bool isFormVisible;

    [ObservableProperty]
    private bool isEditingBooking;

    public string FormTitle => IsEditingBooking ? "Sửa đặt phòng" : "Tạo đặt phòng";

    private BookingRecord? editingBooking;
    private readonly System.Collections.Generic.HashSet<RoomViewModel> observedRooms = new();

    public BookingManager(ObservableCollection<RoomViewModel> rooms)
    {
        this.rooms = rooms;
        Rooms = rooms;
        foreach (var room in Rooms) ObserveRoom(room);
        Rooms.CollectionChanged += OnRoomsCollectionChanged;

        RefreshAvailableRooms();
    }

    [RelayCommand]
    private void OpenForm()
    {
        editingBooking = null;
        IsEditingBooking = false;
        GuestName = string.Empty;
        GuestPhone = string.Empty;
        DurationMinutes = "120";
        SelectedRoom = AvailableRooms.FirstOrDefault();
        ValidationMessage = string.Empty;
        StartTime = DateTime.Now.AddHours(1).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        IsFormVisible = true;
    }

    [RelayCommand]
    private void CloseForm() => IsFormVisible = false;

    [RelayCommand]
    private void CreateBooking()
    {
        if (SelectedRoom is null || !rooms.Contains(SelectedRoom))
        {
            ValidationMessage = "Vui lòng chọn phòng.";
            return;
        }

        if (SelectedRoom.Status != RoomStatus.Available)
        {
            ValidationMessage = "Chỉ có thể đặt phòng đang trống.";
            return;
        }

        if (!DateTime.TryParseExact(StartTime, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start))
        {
            ValidationMessage = "Thời gian không hợp lệ. Dùng định dạng dd/MM/yyyy HH:mm.";
            return;
        }

        start = DateTime.SpecifyKind(start, DateTimeKind.Local);
        if (start <= DateTime.Now)
        {
            ValidationMessage = "Thời gian bắt đầu phải ở tương lai.";
            return;
        }

        if (!int.TryParse(DurationMinutes, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 1 or > 1440)
        {
            ValidationMessage = "Thời lượng phải từ 1 đến 1.440 phút.";
            return;
        }

        if (start > DateTime.MaxValue.AddMinutes(-minutes))
        {
            ValidationMessage = "Thời gian kết thúc vượt quá giới hạn hợp lệ.";
            return;
        }

        var end = start.AddMinutes(minutes);
        var conflict = Bookings.FirstOrDefault(booking =>
            booking != editingBooking && booking.RoomCode == SelectedRoom.Code && !booking.IsCancelled
            && start < booking.EndAt && end > booking.StartAt);

        if (conflict is not null)
        {
            ValidationMessage = $"Trùng lịch với {conflict.GuestName} ({conflict.TimeRange}). Hãy chọn giờ hoặc phòng khác.";
            return;
        }

        var guest = string.IsNullOrWhiteSpace(GuestName) ? "Khách lẻ" : GuestName.Trim();
        if (editingBooking is null)
        {
            var booking = new BookingRecord(this, SelectedRoom.Code, SelectedRoom.Name,
                guest, GuestPhone.Trim(), start, end);
            Bookings.Insert(0, booking);
        }
        else
        {
            editingBooking.Update(SelectedRoom, guest, GuestPhone.Trim(), start, end);
            Bookings.Move(Bookings.IndexOf(editingBooking), 0);
            editingBooking = null;
        }
        ValidationMessage = string.Empty;
        IsFormVisible = false;
    }

    [RelayCommand]
    private void EditBooking(BookingRecord? booking)
    {
        if (booking is null || booking.IsCancelled) return;
        editingBooking = booking;
        IsEditingBooking = true;
        SelectedRoom = Rooms.FirstOrDefault(room => room.Code == booking.RoomCode);
        GuestName = booking.GuestName;
        GuestPhone = booking.GuestPhone;
        StartTime = booking.StartAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        DurationMinutes = ((int)(booking.EndAt - booking.StartAt).TotalMinutes).ToString(CultureInfo.InvariantCulture);
        ValidationMessage = string.Empty;
        IsFormVisible = true;
    }

    [RelayCommand]
    private void CancelBooking(BookingRecord? booking)
    {
        if (booking is null || booking.IsCancelled) return;
        booking.IsCancelled = true;
    }

    public bool HasReservationAtRoomNow(string roomCode)
    {
        var now = DateTime.Now;
        return Bookings.Any(booking => booking.RoomCode == roomCode && !booking.IsCancelled
            && booking.StartAt <= now && booking.EndAt > now);
    }

    private void RefreshAvailableRooms()
    {
        AvailableRooms.Clear();
        foreach (var room in Rooms.Where(room => room.Status == RoomStatus.Available))
            AvailableRooms.Add(room);

        if (SelectedRoom is null || !Rooms.Contains(SelectedRoom) || SelectedRoom.Status != RoomStatus.Available)
            SelectedRoom = AvailableRooms.FirstOrDefault();
    }

    private void OnRoomsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (RoomViewModel room in args.NewItems) ObserveRoom(room);
        RefreshAvailableRooms();
    }

    private void ObserveRoom(RoomViewModel room)
    {
        if (!observedRooms.Add(room)) return;
        room.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RoomViewModel.Status)) RefreshAvailableRooms();
        };
    }

    public bool HasActiveBookingsForRoom(string roomCode) => Bookings.Any(booking =>
        booking.RoomCode == roomCode && !booking.IsCancelled && booking.EndAt > DateTime.Now);

    public void LoadBookings(IEnumerable<BookingData> saved)
    {
        foreach (var item in saved)
        {
            var booking = new BookingRecord(this, item.RoomCode, item.RoomName, item.GuestName, item.GuestPhone, item.StartAt, item.EndAt)
            { IsCancelled = item.IsCancelled };
            Bookings.Add(booking);
        }
    }

    partial void OnIsEditingBookingChanged(bool value) => OnPropertyChanged(nameof(FormTitle));
}

public partial class BookingRecord : ObservableObject
{
    public BookingManager Manager { get; }
    [ObservableProperty]
    private string roomCode;
    [ObservableProperty]
    private string roomName;
    [ObservableProperty]
    private string guestName;
    [ObservableProperty]
    private string guestPhone;
    [ObservableProperty]
    private DateTime startAt;
    [ObservableProperty]
    private DateTime endAt;

    public string TimeRange => $"{StartAt:dd/MM/yyyy HH:mm} – {EndAt:dd/MM/yyyy HH:mm}";
    public bool CanCancel => !IsCancelled;

    [ObservableProperty]
    private bool isCancelled;

    public string StatusText => IsCancelled ? "Đã hủy" : "Đã đặt";

    public BookingRecord(BookingManager manager, string roomCode, string roomName, string guestName,
        string guestPhone, DateTime startAt, DateTime endAt)
    {
        Manager = manager;
        this.roomCode = roomCode;
        this.roomName = roomName;
        this.guestName = guestName;
        this.guestPhone = guestPhone;
        this.startAt = startAt;
        this.endAt = endAt;
    }

    public void Update(RoomViewModel room, string guestName, string guestPhone, DateTime startAt, DateTime endAt)
    {
        RoomCode = room.Code;
        RoomName = room.Name;
        GuestName = guestName;
        GuestPhone = guestPhone;
        StartAt = startAt;
        EndAt = endAt;
        OnPropertyChanged(nameof(TimeRange));
    }

    partial void OnStartAtChanged(DateTime value) => OnPropertyChanged(nameof(TimeRange));
    partial void OnEndAtChanged(DateTime value) => OnPropertyChanged(nameof(TimeRange));

    partial void OnIsCancelledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanCancel));
    }
}
