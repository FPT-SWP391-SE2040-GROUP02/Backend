namespace LegacyVault.Application.Dtos.Requests
{
    /// <summary>Admin tạo hoặc sửa một thư bổ sung. Luật kiểm tra nằm ở ExtraMailService.</summary>
    public class SaveExtraMailRequest
    {
        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        /// <summary>Âm là trước hạn điểm danh, dương là sau hạn.</summary>
        public int DaysFromDue { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public string ButtonText { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }
}

namespace LegacyVault.Application.Dtos.Responses
{
    public class ExtraMailResponse
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; }

        public int DaysFromDue { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public string ButtonText { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; set; }

        public string? UpdatedByName { get; set; }
    }

    /// <summary>Danh sách thư bổ sung cùng giới hạn và biến chèn được, để màn hình không phải đoán.</summary>
    public class ExtraMailListResponse
    {
        public List<ExtraMailResponse> Items { get; set; } = new List<ExtraMailResponse>();

        public int MaxItems { get; set; }

        public int MinDaysFromDue { get; set; }

        public int MaxDaysFromDue { get; set; }

        public List<EmailTemplateTokenResponse> Tokens { get; set; } = new List<EmailTemplateTokenResponse>();
    }
}
