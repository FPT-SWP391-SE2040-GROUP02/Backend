using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Security;

/// <summary>
/// Mã hóa phong bì (Envelope Encryption) cho mảnh khóa Người thực thi (ExecutorKeyShard).
/// Cho phép lưu trữ bảo mật để gửi trực tiếp cho Người thực thi trên hệ thống mà không cần phiếu giấy.
/// </summary>
public interface IExecutorKeyProtector
{
    /// <summary>
    /// Mã hóa chuỗi shard và gắn vào entity (EncryptedShard, EncryptionKeyId, WrappedDataKey).
    /// </summary>
    void Protect(ExecutorKeyShard shard, string plainShard);

    /// <summary>
    /// Giải mã mảnh khóa đã lưu trong entity. Trả về rỗng nếu chưa có mảnh mã hóa.
    /// </summary>
    string Unprotect(ExecutorKeyShard shard);
}
