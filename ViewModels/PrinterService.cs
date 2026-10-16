using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace KaraokeManager.ViewModels;

public sealed record PrinterStatus(bool IsConnected, string Name, string Type, string Message)
{
    public string DisplayText => IsConnected ? $"Máy in: {Name} · {Type} · Đã kết nối" : Message;
}

public static class PrinterService
{
    public static PrinterStatus Detect()
    {
        if (OperatingSystem.IsWindows()) return DetectWindows();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return DetectCups();
        return new(false, "", "", "Không hỗ trợ kiểm tra máy in trên hệ điều hành này.");
    }

    private static PrinterStatus DetectCups()
    {
        try
        {
            var defaultPrinter = Run("lpstat", "-d");
            var name = defaultPrinter.Output.Split('\n').Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("system default destination:", StringComparison.OrdinalIgnoreCase))?
                .Split(':', 2).Last().Trim();
            if (defaultPrinter.ExitCode != 0 || string.IsNullOrWhiteSpace(name))
                return new(false, "", "", "Chưa phát hiện máy in mặc định.");

            var queue = Run("lpstat", "-p", name);
            var device = Run("lpstat", "-v", name);
            var lower = queue.Output.ToLowerInvariant();
            if (queue.ExitCode != 0 || lower.Contains("disabled") || lower.Contains("not found") || lower.Contains("offline"))
                return new(false, name, "", $"Máy in {name} chưa sẵn sàng.");

            var uri = device.Output.ToLowerInvariant();
            var type = uri.Contains("usb") ? "USB" :
                uri.Contains("ipp") || uri.Contains("socket") || uri.Contains("lpd") || uri.Contains("dnssd") ? "Mạng" : "Hàng đợi hệ thống";
            return new(true, name, type, "Máy in đã kết nối.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            return new(false, "", "", "Không kiểm tra được máy in: dịch vụ in chưa sẵn sàng.");
        }
    }

    private static (int ExitCode, string Output) Run(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Không mở được dịch vụ in.");
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(2500)) { process.Kill(true); throw new InvalidOperationException("Hết thời gian chờ dịch vụ in."); }
        return (process.ExitCode, output);
    }

    private static PrinterStatus DetectWindows()
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            var size = 0;
            GetDefaultPrinter(null, ref size);
            if (size == 0) return new(false, "", "", "Chưa phát hiện máy in mặc định.");
            var nameBuffer = new System.Text.StringBuilder(size);
            if (!GetDefaultPrinter(nameBuffer, ref size)) return new(false, "", "", "Chưa phát hiện máy in mặc định.");
            var name = nameBuffer.ToString();
            if (string.IsNullOrWhiteSpace(name) || !OpenPrinter(name, out handle, IntPtr.Zero))
                return new(false, "", "", "Chưa phát hiện máy in mặc định.");
            var sizeNeeded = 0;
            GetPrinter(handle, 6, IntPtr.Zero, 0, ref sizeNeeded);
            var buffer = Marshal.AllocHGlobal(sizeNeeded);
            try
            {
                if (!GetPrinter(handle, 6, buffer, sizeNeeded, ref sizeNeeded)) return new(false, name, "", "Không thể xác nhận trạng thái máy in.");
                var status = unchecked((uint)Marshal.ReadInt32(buffer));
                if ((status & (0x00000002u | 0x00000080u | 0x00001000u | 0x00000001u)) != 0)
                    return new(false, name, "", $"Máy in {name} đang ngoại tuyến hoặc tạm dừng.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
            var port = name.StartsWith("\\\\", StringComparison.Ordinal) ? "Mạng" : "Hàng đợi hệ thống";
            return new(true, name, port, "Máy in đã được hệ thống nhận diện.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            return new(false, "", "", "Không kiểm tra được máy in mặc định.");
        }
        finally { if (handle != IntPtr.Zero) ClosePrinter(handle); }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(System.Text.StringBuilder? buffer, ref int size);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool GetPrinter(IntPtr handle, int level, IntPtr buffer, int size, ref int needed);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);
}
