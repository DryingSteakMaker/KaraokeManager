using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

public partial class SalesManager : ObservableObject
{
    private readonly ObservableCollection<ProductRecord> products;
    private readonly ObservableCollection<RoomViewModel> rooms;
    private readonly Dictionary<string, ObservableCollection<OrderLineRecord>> orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<RoomViewModel> observedRooms = new();

    public ObservableCollection<RoomViewModel> ActiveRooms { get; } = new();
    public ObservableCollection<OrderLineRecord> CurrentOrderLines { get; } = new();
    public IReadOnlyList<ProductRecord> Products => products;
    public decimal TotalOpenOrderValue => orders.Values.SelectMany(order => order).Sum(line => line.LineTotal);

    [ObservableProperty] private RoomViewModel? selectedRoom;
    [ObservableProperty] private ProductRecord? selectedProduct;
    [ObservableProperty] private string quantityInput = "1";
    [ObservableProperty] private string message = string.Empty;

    public decimal CurrentOrderTotal => CurrentOrderLines.Sum(line => line.LineTotal);

    public SalesManager(ObservableCollection<ProductRecord> products, ObservableCollection<RoomViewModel> rooms)
    {
        this.products = products;
        this.rooms = rooms;
        foreach (var room in rooms) ObserveRoom(room);
        rooms.CollectionChanged += OnRoomsChanged;
        RefreshActiveRooms();
        products.CollectionChanged += (_, _) => RefreshSelectedProduct();
        CurrentOrderLines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CurrentOrderTotal));
        RefreshSelectedProduct();
    }

    public void SelectRoom(RoomViewModel room)
    {
        SelectedRoom = room;
        if (!orders.TryGetValue(room.Code, out var lines))
        {
            lines = GetRoomOrder(room);
        }
        CurrentOrderLines.Clear();
        foreach (var line in lines) CurrentOrderLines.Add(line);
        RefreshSelectedProduct();
        Message = string.Empty;
    }

    [RelayCommand]
    private void AddOrderLine()
    {
        if (SelectedRoom is null || SelectedRoom.Status != RoomStatus.InUse)
        {
            Message = "Chỉ có thể gọi món cho phòng đang sử dụng.";
            return;
        }
        if (SelectedProduct is null || !products.Any(product => product.Code == SelectedProduct.Code))
        {
            Message = "Vui lòng chọn sản phẩm.";
            return;
        }
        if (!int.TryParse(QuantityInput, out var quantity) || quantity < 1)
        {
            Message = "Số lượng phải là số nguyên dương.";
            return;
        }

        var product = products.First(item => item.Code == SelectedProduct.Code);

        var roomOrder = GetRoomOrder(SelectedRoom);
        var existing = roomOrder.FirstOrDefault(line => line.ProductCode == product.Code);
        if (existing is null)
        {
            existing = new OrderLineRecord(product.Code, product.Name, product.Unit, product.SalePrice, product.CostPrice, quantity);
            existing.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(OrderLineRecord.Quantity) or nameof(OrderLineRecord.LineTotal))
                {
                    OnPropertyChanged(nameof(CurrentOrderTotal));
                    OnPropertyChanged(nameof(TotalOpenOrderValue));
                }
            };
            roomOrder.Add(existing);
        }
        else
            existing.Quantity += quantity;

        QuantityInput = "1";
        Message = "Đã thêm món vào phòng.";
        RefreshVisibleOrder(roomOrder);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
        RefreshSelectedProduct();
    }

    public void AddOrderLineForRoom(RoomViewModel room, ProductRecord product, int quantity = 1)
    {
        var previousRoom = SelectedRoom;
        var previousProduct = SelectedProduct;
        var previousQuantity = QuantityInput;
        SelectedRoom = room;
        SelectedProduct = product;
        QuantityInput = quantity.ToString();
        AddOrderLine();
        SelectedRoom = previousRoom;
        SelectedProduct = previousProduct;
        QuantityInput = previousQuantity;
    }

    [RelayCommand]
    private void RemoveOrderLine(OrderLineRecord? line)
    {
        if (line is null || SelectedRoom is null) return;
        var roomOrder = GetRoomOrder(SelectedRoom);
        if (!roomOrder.Remove(line)) return;
        RefreshVisibleOrder(roomOrder);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public void RemoveOrderLineForRoom(RoomViewModel room, OrderLineRecord? line)
    {
        if (line is null) return;
        var roomOrder = GetRoomOrder(room);
        if (!roomOrder.Remove(line)) return;
        RefreshVisibleOrder(roomOrder);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public void UpdateOrderLineQuantityForRoom(RoomViewModel room, OrderLineRecord line, int quantity)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity));
        var roomOrder = GetRoomOrder(room);
        if (!roomOrder.Contains(line)) return;
        line.Quantity = quantity;
        RefreshVisibleOrder(roomOrder);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public ObservableCollection<OrderLineRecord> GetOrderLinesForRoom(RoomViewModel room) => GetRoomOrder(room);

    public IReadOnlyList<OrderLineSnapshot> GetOrderSnapshot(RoomViewModel room) =>
        GetRoomOrder(room).Select(line => new OrderLineSnapshot(line.ProductCode, line.Name, line.Unit,
            line.UnitSalePrice, line.UnitCostPrice, line.Quantity)).ToList();

    public void CompleteOrder(RoomViewModel room)
    {
        var lines = GetRoomOrder(room);
        lines.Clear();
        RefreshVisibleOrder(lines);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public void TransferOrder(RoomViewModel source, RoomViewModel destination)
    {
        if (!orders.Remove(source.Code, out var sourceLines) || sourceLines.Count == 0) return;
        var destinationLines = GetRoomOrder(destination);
        foreach (var line in sourceLines.ToList())
        {
            var existing = destinationLines.FirstOrDefault(item => item.ProductCode == line.ProductCode);
            if (existing is null) destinationLines.Add(line);
            else existing.Quantity += line.Quantity;
        }
        sourceLines.Clear();
        if (SelectedRoom is not null && (SelectedRoom == source || SelectedRoom == destination)) SelectRoom(destination);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public void LoadOpenOrders(IEnumerable<OpenOrderData> saved)
    {
        foreach (var order in saved)
        {
            var room = rooms.FirstOrDefault(item => item.Code == order.RoomCode);
            if (room is null || order.Lines.Count == 0) continue;
            var lines = GetRoomOrder(room);
            foreach (var line in order.Lines)
            {
                var restored = new OrderLineRecord(line.ProductCode, line.Name, line.Unit, line.UnitSalePrice, line.UnitCostPrice, line.Quantity);
                restored.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName is nameof(OrderLineRecord.Quantity) or nameof(OrderLineRecord.LineTotal))
                    {
                        OnPropertyChanged(nameof(CurrentOrderTotal));
                        OnPropertyChanged(nameof(TotalOpenOrderValue));
                    }
                };
                lines.Add(restored);
            }
        }
        if (SelectedRoom is not null && orders.TryGetValue(SelectedRoom.Code, out var selectedLines))
            RefreshVisibleOrder(selectedLines);
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public void ClearOpenOrders()
    {
        orders.Clear();
        CurrentOrderLines.Clear();
        SelectedRoom = null;
        OnPropertyChanged(nameof(CurrentOrderTotal));
        OnPropertyChanged(nameof(TotalOpenOrderValue));
    }

    public IReadOnlyList<OpenOrderData> SnapshotOpenOrders() => orders
        .Where(pair => pair.Value.Count > 0)
        .Select(pair => new OpenOrderData(pair.Key, pair.Value.Select(line => new OrderLineSnapshot(line.ProductCode,
            line.Name, line.Unit, line.UnitSalePrice, line.UnitCostPrice, line.Quantity)).ToList())).ToList();

    public bool IsProductInOpenOrder(string productCode) => orders.Values.Any(order => order.Any(line => line.ProductCode == productCode));

    private ObservableCollection<OrderLineRecord> GetRoomOrder(RoomViewModel room)
    {
        if (!orders.TryGetValue(room.Code, out var lines))
        {
            lines = new ObservableCollection<OrderLineRecord>();
            orders.Add(room.Code, lines);
            lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalOpenOrderValue));
        }
        return lines;
    }

    private void RefreshVisibleOrder(ObservableCollection<OrderLineRecord> lines)
    {
        if (SelectedRoom is null || !ReferenceEquals(lines, GetRoomOrder(SelectedRoom))) return;
        CurrentOrderLines.Clear();
        foreach (var line in lines) CurrentOrderLines.Add(line);
        OnPropertyChanged(nameof(CurrentOrderTotal));
    }

    private void RefreshSelectedProduct()
    {
        if (SelectedProduct is not null)
            SelectedProduct = products.FirstOrDefault(product => product.Code == SelectedProduct.Code);
        if (SelectedProduct is null) SelectedProduct = products.FirstOrDefault();
    }

    private void RefreshActiveRooms()
    {
        ActiveRooms.Clear();
        foreach (var room in rooms.Where(room => room.Status == RoomStatus.InUse)) ActiveRooms.Add(room);
        if (SelectedRoom is not null && SelectedRoom.Status != RoomStatus.InUse) SelectedRoom = null;
    }

    private void OnRoomsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (RoomViewModel room in args.NewItems) ObserveRoom(room);
        RefreshActiveRooms();
    }

    private void ObserveRoom(RoomViewModel room)
    {
        if (!observedRooms.Add(room)) return;
        room.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RoomViewModel.Status)) RefreshActiveRooms();
        };
    }

    partial void OnSelectedRoomChanged(RoomViewModel? value)
    {
        if (value is not null && orders.TryGetValue(value.Code, out var lines))
        {
            CurrentOrderLines.Clear();
            foreach (var line in lines) CurrentOrderLines.Add(line);
        }
        else if (value is null)
            CurrentOrderLines.Clear();
        OnPropertyChanged(nameof(CurrentOrderTotal));
    }
}

public partial class OrderLineRecord : ObservableObject
{
    public string ProductCode { get; }
    public string Name { get; }
    public string Unit { get; }
    public decimal UnitSalePrice { get; }
    public decimal UnitCostPrice { get; }

    [ObservableProperty] private int quantity;

    public decimal LineTotal => UnitSalePrice * Quantity;
    public string UnitPriceText => $"{UnitSalePrice:N0} ₫/{Unit}";
    public string LineTotalText => $"{LineTotal:N0} ₫";

    public OrderLineRecord(string productCode, string name, string unit, decimal unitSalePrice, decimal unitCostPrice, int quantity)
    {
        ProductCode = productCode;
        Name = name;
        Unit = unit;
        UnitSalePrice = unitSalePrice;
        UnitCostPrice = unitCostPrice;
        this.quantity = quantity;
    }

    partial void OnQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(LineTotalText));
    }
}

public sealed record OrderLineSnapshot(string ProductCode, string Name, string Unit,
    decimal UnitSalePrice, decimal UnitCostPrice, int Quantity)
{
    public decimal LineTotal => UnitSalePrice * Quantity;
}
