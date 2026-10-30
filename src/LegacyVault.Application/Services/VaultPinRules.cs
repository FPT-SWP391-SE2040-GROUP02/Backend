using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>Luật chọn PIN và các mốc khóa. Hàm thuần, không đọc DB.</summary>
public static class VaultPinRules
{
    public const int PinLength = 6;

    /// <summary>Số lần sai liên tiếp trước khi bị khóa.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Thời gian khóa theo bậc: lần khóa thứ nhất 15 phút, thứ hai 1 giờ, từ lần thứ ba 24 giờ.</summary>
    private static readonly TimeSpan[] LockDurations =
    [
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(24),
    ];

    /// <summary>Bậc khóa tối đa (khóa bậc này lặp lại cho các lần sau).</summary>
    public static int MaxLockLevel => LockDurations.Length;

    /// <summary>Thời gian khóa của bậc level (1..MaxLockLevel).</summary>
    public static TimeSpan LockDuration(int level)
    {
        int index = Math.Clamp(level, 1, LockDurations.Length) - 1;
        return LockDurations[index];
    }

    /// <summary>true khi đúng 6 chữ số 0-9.</summary>
    public static bool IsWellFormed(string? pin)
    {
        if (pin is null || pin.Length != PinLength)
        {
            return false;
        }

        foreach (char c in pin)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Kiểm PIN mới khi đặt, đổi hoặc đặt lại: đúng 6 chữ số, khớp ô nhập lại, không quá dễ đoán.
    /// </summary>
    public static void EnsureAcceptable(string pin, string confirmPin, string pinField, string confirmField)
    {
        if (!IsWellFormed(pin))
        {
            throw ValidationErrors.For(pinField, "Mã PIN gồm đúng 6 chữ số.");
        }
        if (!string.Equals(pin, confirmPin, StringComparison.Ordinal))
        {
            throw ValidationErrors.For(confirmField, "Mã PIN nhập lại chưa khớp.");
        }
        if (IsTooWeak(pin))
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.VaultPinTooWeak,
                "Mã PIN quá dễ đoán. Vui lòng không dùng các số giống nhau hoặc dãy số liên tiếp.");
        }
    }

    /// <summary>Quá dễ đoán: 6 chữ số giống nhau, hoặc dãy tăng/giảm liên tiếp (123456, 654321, 012345...).</summary>
    public static bool IsTooWeak(string pin)
    {
        bool allSame = true;
        bool ascending = true;
        bool descending = true;
        for (int i = 1; i < pin.Length; i++)
        {
            int step = pin[i] - pin[i - 1];
            allSame &= step == 0;
            ascending &= step == 1;
            descending &= step == -1;
        }
        return allSame || ascending || descending;
    }
}
