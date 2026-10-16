using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KaraokeManager.ViewModels;

/// <summary>Small local database for a single-store installation. Writes atomically to the app data folder.</summary>
public sealed class LocalDatabase
{
    private readonly string path;
    private readonly string backupFolder;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public DatabaseState State { get; private set; }
    public string Message { get; private set; } = string.Empty;

    public LocalDatabase()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaraokeManager");
        path = Path.Combine(folder, "karaoke-data.json");
        backupFolder = Path.Combine(folder, "Backups");
        try
        {
            State = File.Exists(path) ? JsonSerializer.Deserialize<DatabaseState>(File.ReadAllText(path), JsonOptions) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            State = new();
            Message = $"Không đọc được dữ liệu đã lưu: {e.Message}";
        }
    }

    public bool Save(DatabaseState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            CreateDailyBackup();
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
            State = state;
            Message = "";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Không thể lưu dữ liệu: {e.Message}";
            return false;
        }
    }

    public IReadOnlyList<string> GetBackups()
    {
        if (!Directory.Exists(backupFolder)) return Array.Empty<string>();
        return Directory.GetFiles(backupFolder, "karaoke-data-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
    }

    public bool CreateBackup()
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(State, JsonOptions));
            }
            CreateBackupFile();
            Message = "Đã tạo bản sao lưu.";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Không thể tạo bản sao lưu: {e.Message}";
            return false;
        }
    }

    public DatabaseState? RestoreBackup(string backupPath)
    {
        try
        {
            var fullBackupPath = Path.GetFullPath(backupPath);
            var fullBackupFolder = Path.GetFullPath(backupFolder) + Path.DirectorySeparatorChar;
            if (!fullBackupPath.StartsWith(fullBackupFolder, StringComparison.Ordinal)
                || !File.Exists(fullBackupPath))
            {
                Message = "Không tìm thấy bản sao lưu đã chọn.";
                return null;
            }
            var restored = JsonSerializer.Deserialize<DatabaseState>(File.ReadAllText(fullBackupPath), JsonOptions);
            if (restored is null) { Message = "Bản sao lưu không có dữ liệu hợp lệ."; return null; }
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(restored, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
            State = restored;
            Message = "Đã khôi phục dữ liệu từ bản sao lưu.";
            return restored;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            Message = $"Không thể khôi phục bản sao lưu: {e.Message}";
            return null;
        }
    }

    private void CreateDailyBackup()
    {
        if (!File.Exists(path)) return;
        var today = DateTime.Now.ToString("yyyyMMdd");
        if (Directory.Exists(backupFolder) && Directory.GetFiles(backupFolder, $"karaoke-data-{today}-*.json").Length > 0) return;
        CreateBackupFile();
    }

    private void CreateBackupFile()
    {
        Directory.CreateDirectory(backupFolder);
        var name = $"karaoke-data-{DateTime.Now:yyyyMMdd-HHmmssfff}.json";
        File.Copy(path, Path.Combine(backupFolder, name), overwrite: false);
        var oldBackups = Directory.GetFiles(backupFolder, "karaoke-data-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc).Skip(30).ToArray();
        foreach (var oldBackup in oldBackups) File.Delete(oldBackup);
    }
}

public sealed class DatabaseState
{
    public List<RoomData> Rooms { get; set; } = new();
    public List<BookingData> Bookings { get; set; } = new();
    public List<ProductRecord> Products { get; set; } = new();
    public List<string> Categories { get; set; } = new() { "Đồ uống", "Đồ ăn" };
    public List<InvoiceRecord> Invoices { get; set; } = new();
    public List<OpenOrderData> OpenOrders { get; set; } = new();
    public int RoundingMinutes { get; set; } = 15;
    public decimal DefaultHourlyRate { get; set; } = 110000;
    public decimal SingerHourlyRate { get; set; } = 100000;
    public string ShopName { get; set; } = "Karaoke Đức Hiếu";
    public string ShopAddress { get; set; } = string.Empty;
    public string ShopPhone { get; set; } = string.Empty;
    public string ShopLogoPath { get; set; } = string.Empty;
    public string InvoiceGreeting { get; set; } = "Cảm ơn quý khách và hẹn gặp lại!";
    public string InvoiceFooter { get; set; } = string.Empty;
    public string InvoiceNumberFormat { get; set; } = "Theo thời gian";
    public string AccentColor { get; set; } = "Xanh lá";
    public string ThemeMode { get; set; } = "Sáng";
    public string FontSizeOption { get; set; } = "Vừa";
}

public sealed record RoomData(string Code, string Name, string Type, int Capacity, RoomStatus Status,
    DateTime? StartedAt, decimal HourlyRate, decimal? PeakHourlyRate, string PeakStart, string PeakEnd,
    int SingerCount = 0, DateTime? SingerStartedAt = null, DateTime? SingerEndedAt = null);
public sealed record BookingData(string RoomCode, string RoomName, string GuestName, string GuestPhone,
    DateTime StartAt, DateTime EndAt, bool IsCancelled);
public sealed record OpenOrderData(string RoomCode, List<OrderLineSnapshot> Lines);
