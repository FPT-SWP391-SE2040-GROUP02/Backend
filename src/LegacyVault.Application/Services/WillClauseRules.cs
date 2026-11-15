using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>
/// Điều khoản Chủ kho tự thêm vào di chúc. Lưu một cột văn bản, mỗi điều một dòng; mỗi điều chỉ một đoạn.
/// </summary>
public static class WillClauseRules
{
    public const int MaxClauses = 10;
    public const int MaxClauseLength = 500;

    private const char Separator = '\n';

    /// <summary>Kiểm tra và gộp danh sách điều khoản thành một chuỗi lưu DB; danh sách rỗng trả null.</summary>
    public static string? Join(IReadOnlyList<string> clauses)
    {
        List<string> cleaned = new List<string>();
        foreach (string raw in clauses)
        {
            string clause = Collapse(raw);
            if (clause.Length == 0)
            {
                throw ValidationErrors.For("willClauses", "Điều khoản không được để trống.");
            }
            if (clause.Length > MaxClauseLength)
            {
                throw ValidationErrors.For("willClauses", $"Mỗi điều khoản tối đa {MaxClauseLength} ký tự.");
            }
            cleaned.Add(clause);
        }
        if (cleaned.Count > MaxClauses)
        {
            throw ValidationErrors.For("willClauses", $"Tối đa {MaxClauses} điều khoản.");
        }
        if (cleaned.Count == 0)
        {
            return null;
        }
        return string.Join(Separator, cleaned);
    }

    /// <summary>Tách chuỗi lưu DB thành danh sách điều khoản; chưa có thì danh sách rỗng.</summary>
    public static List<string> Split(string? stored)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(stored))
        {
            return result;
        }
        foreach (string part in stored.Split(Separator))
        {
            if (part.Length > 0)
            {
                result.Add(part);
            }
        }
        return result;
    }

    /// <summary>Gộp mọi khoảng trắng và xuống dòng thành một dấu cách để mỗi điều luôn là một dòng.</summary>
    private static string Collapse(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
