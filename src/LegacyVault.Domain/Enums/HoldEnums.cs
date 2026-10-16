namespace LegacyVault.Domain.Enums;

/// <summary>Phạm vi của một hold (SRS §6.4): hold chặn đúng đối tượng này.</summary>
public enum HoldScope
{
    Vault = 0,
    Container = 1,
    Designation = 2,
    Claim = 3,
    Grant = 4,
}

/// <summary>Loại hold.</summary>
public enum HoldType
{
    /// <summary>Owner báo còn sống (F2C): đặt tự động, gỡ khi Verifier kết luận.</summary>
    OwnerAlive = 0,
    /// <summary>Tranh chấp claim hoặc nghi vấn do nhân viên đặt.</summary>
    Dispute = 1,
    /// <summary>Admin đặt thủ công, có lý do và ngày xem lại.</summary>
    Admin = 2,
}

public enum HoldStatus
{
    Active = 0,
    Released = 1,
}
