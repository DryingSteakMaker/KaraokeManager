using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KaraokeManager.ViewModels;

public partial class AccountManager : ObservableObject
{
    public const string DefaultUserName = "Admin";
    private readonly string storePath;
    private AccountRecord account;

    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string userNameInput = DefaultUserName;
    [ObservableProperty] private string newUserName = string.Empty;
    [ObservableProperty] private string currentPassword = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private string? currentUser;

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(CurrentUser);
    public string LoginName => account.UserName;

    public AccountManager()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaraokeManager");
        storePath = Path.Combine(directory, "accounts.json");
        account = LoadAccount();
        UserNameInput = account.UserName;
        NewUserName = account.UserName;
    }

    [RelayCommand]
    private void SignIn()
    {
        if (!account.UserName.Equals(UserNameInput.Trim(), StringComparison.OrdinalIgnoreCase)
            || !VerifyPassword(Password, account.Salt, account.PasswordHash))
        {
            Message = "Tên đăng nhập hoặc mật khẩu không đúng.";
            return;
        }
        CurrentUser = account.UserName;
        Password = string.Empty;
        Message = string.Empty;
    }

    [RelayCommand]
    private void ChangePassword()
    {
        if (!VerifyPassword(CurrentPassword, account.Salt, account.PasswordHash))
        {
            Message = "Mật khẩu hiện tại không đúng.";
            return;
        }
        if (NewPassword.Length < 3)
        {
            Message = "Mật khẩu mới cần ít nhất 3 ký tự.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            Message = "Mật khẩu xác nhận chưa khớp.";
            return;
        }

        var oldAccount = account;
        var salt = RandomNumberGenerator.GetBytes(16);
        account = new AccountRecord(oldAccount.UserName, Convert.ToBase64String(salt), HashPassword(NewPassword, salt));
        if (!SaveAccount())
        {
            account = oldAccount;
            return;
        }
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        Message = "Đã đổi mật khẩu.";
    }

    [RelayCommand]
    private void ChangeUserName()
    {
        var value = NewUserName.Trim();
        if (!VerifyPassword(CurrentPassword, account.Salt, account.PasswordHash))
        {
            Message = "Nhập đúng mật khẩu hiện tại để đổi tên đăng nhập.";
            return;
        }
        if (value.Length is < 2 or > 40 || value.Any(char.IsControl))
        {
            Message = "Tên đăng nhập cần từ 2 đến 40 ký tự.";
            return;
        }
        var previous = account;
        account = account with { UserName = value };
        if (!SaveAccount()) { account = previous; return; }
        UserNameInput = value;
        NewUserName = value;
        if (IsAuthenticated) CurrentUser = value;
        OnPropertyChanged(nameof(LoginName));
        CurrentPassword = string.Empty;
        Message = "Đã đổi tên đăng nhập.";
    }

    [RelayCommand]
    private void SignOut()
    {
        CurrentUser = null;
        Password = string.Empty;
        Message = string.Empty;
    }

    private AccountRecord LoadAccount()
    {
        if (File.Exists(storePath))
        {
            try
            {
                // Migrate the previous multi-account format to the single Admin account,
                // preserving the Admin password when one already exists.
                var legacyAccounts = JsonSerializer.Deserialize<List<AccountRecord>>(File.ReadAllText(storePath));
                var existing = legacyAccounts?.FirstOrDefault(item => string.Equals(item.UserName, DefaultUserName, StringComparison.OrdinalIgnoreCase))
                    ?? legacyAccounts?.FirstOrDefault();
                if (existing is not null && IsValidRecord(existing))
                {
                    account = existing with { UserName = string.IsNullOrWhiteSpace(existing.UserName) ? DefaultUserName : existing.UserName.Trim() };
                    if (legacyAccounts!.Count != 1 || !string.Equals(existing.UserName, account.UserName, StringComparison.Ordinal)) SaveAccount();
                    return account;
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                Message = "Không đọc được thiết lập mật khẩu; đang khởi tạo mật khẩu mặc định.";
            }
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var initial = new AccountRecord(DefaultUserName, Convert.ToBase64String(salt), HashPassword("123", salt));
        account = initial;
        SaveAccount();
        return initial;
    }

    private static bool IsValidRecord(AccountRecord record)
    {
        try
        {
            return Convert.FromBase64String(record.Salt).Length > 0 && Convert.FromBase64String(record.PasswordHash).Length > 0;
        }
        catch (FormatException) { return false; }
    }

    private bool SaveAccount()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            File.WriteAllText(storePath, JsonSerializer.Serialize(new[] { account }));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Message = $"Không thể lưu thông tin tài khoản: {exception.Message}";
            return false;
        }
    }

    private static string HashPassword(string value, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(value, salt, 120_000, HashAlgorithmName.SHA256, 32));

    private static bool VerifyPassword(string value, string saltText, string hash)
    {
        try
        {
            var salt = Convert.FromBase64String(saltText);
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(value, salt, 120_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    partial void OnCurrentUserChanged(string? value) => OnPropertyChanged(nameof(IsAuthenticated));

    public sealed record AccountRecord(string UserName, string Salt, string PasswordHash);
}
