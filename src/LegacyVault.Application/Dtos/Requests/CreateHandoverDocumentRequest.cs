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
