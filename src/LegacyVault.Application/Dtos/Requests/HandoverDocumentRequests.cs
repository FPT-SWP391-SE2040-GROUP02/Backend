using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Người nhận chọn Chấp nhận hoặc Từ chối để bắt đầu biên bản (POST /api/v1/handover/turns/{id}/document).</summary>
public class CreateHandoverDocumentRequest
{
    /// <summary>ACCEPT hoặc DECLINE.</summary>
    [Required(ErrorMessage = "Vui lòng chọn Chấp nhận hoặc Từ chối.")]
    [RegularExpression("^(ACCEPT|DECLINE)$", ErrorMessage = "Lựa chọn không hợp lệ.")]
    public string Kind { get; set; } = string.Empty;
}

/// <summary>Người xét duyệt kiểm tra thủ công rồi gửi (POST /api/v1/verifier/handover-submissions/{id}/approve với biên bản).</summary>
public class ApproveHandoverDocumentRequest
{
    /// <summary>Bốn mục kiểm tra thủ công, phải tick đủ cả bốn (true).</summary>
    public List<bool> Checks { get; set; } = new List<bool>();

    /// <summary>Phải true: Người xét duyệt tự chịu trách nhiệm về kết quả kiểm tra.</summary>
    public bool Acknowledged { get; set; }
}
