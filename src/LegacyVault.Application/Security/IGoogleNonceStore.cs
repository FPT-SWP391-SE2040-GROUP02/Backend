namespace LegacyVault.Application.Security;

/// <summary>
/// Nonce do Backend phát cho nút Đăng nhập Google, dùng đúng một lần và hết hạn nhanh. FE truyền nonce này cho Google
/// nên ID token chứa nó; token bị lấy cắp hoặc phát lại sau khi nonce đã dùng thì bị từ chối.
/// </summary>
public interface IGoogleNonceStore
{
    /// <summary>Phát nonce mới. Quá nhiều nonce chưa dùng thì ném lỗi 503 thay vì phình bộ nhớ.</summary>
    string Issue();

    /// <summary>Đánh dấu đã dùng. Trả true đúng một lần cho nonce do Backend phát còn hạn; còn lại trả false.</summary>
    bool TryConsume(string nonce);
}
