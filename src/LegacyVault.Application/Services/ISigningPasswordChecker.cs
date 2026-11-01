using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// DEATH-03: kiểm mật khẩu xác nhận duyệt/từ chối của Verifier kèm khóa lũy tiến (giống PIN kho).
/// </summary>
public interface ISigningPasswordChecker
{
    /// <summary>
    /// Đang khóa → 423 SIGNING_LOCKED (kể cả nhập đúng). Tài khoản chưa có mật khẩu → 403 SIGNING_PASSWORD_INVALID
    /// (không tính là một lần đoán). Đúng → bộ đếm về 0, chưa lưu (người gọi lưu cùng quyết định).
    /// Sai → tăng bộ đếm / khóa, LƯU NGAY rồi ném 403 kèm attemptsRemaining hoặc 423 kèm lockedUntil.
    /// Lưu bộ đếm bị tranh chấp → 409 EDIT_CONFLICT.
    /// </summary>
    Task CheckAsync(User user, string password, DateTimeOffset now, CancellationToken ct);
}
