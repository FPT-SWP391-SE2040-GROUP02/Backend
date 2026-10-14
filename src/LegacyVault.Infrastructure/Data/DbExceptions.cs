using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Data;

public static class DbExceptions
{
    /// <summary>SQL Server 2601/2627: vi phạm unique index/constraint (request khác vừa ghi trước).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    /// <summary>Bỏ theo dõi các entity chưa lưu được, để lần SaveChanges sau của request không thử INSERT lại.</summary>
    public static void DetachAll(DbContext context, params object[] entities)
    {
        foreach (var entity in entities)
        {
            context.Entry(entity).State = EntityState.Detached;
        }
    }
}
    