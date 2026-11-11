using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ truy cập dữ liệu theo IPaymentTransactionRepository; luật đối chiếu nằm ở SePayWebhookService/SePayRules.</remarks>
public sealed class PaymentTransactionRepository : IPaymentTransactionRepository
{
    private readonly AppDbContext context;

    public PaymentTransactionRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<PaymentTransaction?> FindByProviderTransactionIdAsync(
        string provider, long providerTransactionId, CancellationToken ct)
    {
        return context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.Provider == provider && t.ProviderTransactionId == providerTransactionId, ct);
    }

    public async Task<(List<PaymentTransaction> Items, int Total)> ListAsync(
        PaymentTransactionStatus? status, string? search, int pageIndex, int pageSize, CancellationToken ct,
        PaymentTransactionFilter? filter = null)
    {
        IQueryable<PaymentTransaction> query = context.PaymentTransactions.AsNoTracking();
        if (filter != null && filter.SuccessOnly)
        {
            query = query.Where(t => t.Status == PaymentTransactionStatus.Matched || t.Status == PaymentTransactionStatus.Resolved);
        }
        else if (status != null)
        {
            query = query.Where(t => t.Status == status.Value);
        }
        if (filter != null && !string.IsNullOrEmpty(filter.Environment))
        {
            string environment = filter.Environment;
            query = query.Where(t => t.Environment == environment);
        }
        if (filter != null && filter.From != null)
        {
            DateTimeOffset from = filter.From.Value;
            query = query.Where(t => t.ReceivedAt >= from);
        }
        if (filter != null && filter.ToExclusive != null)
        {
            DateTimeOffset to = filter.ToExclusive.Value;
            query = query.Where(t => t.ReceivedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(t =>
                (t.MatchedPaymentCode != null && t.MatchedPaymentCode.Contains(s)) ||
                (t.ReferenceCode != null && t.ReferenceCode.Contains(s)) ||
                (t.Code != null && t.Code.Contains(s)) ||
                t.ProviderTransactionId.ToString().Contains(s));
        }

        int total = await query.CountAsync(ct);
        List<PaymentTransaction> items = await query
            .OrderByDescending(t => t.ReceivedAt)
            .ThenByDescending(t => t.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<List<RevenueTransaction>> ListSuccessAmountsAsync(PaymentTransactionFilter filter, CancellationToken ct)
    {
        IQueryable<PaymentTransaction> query = context.PaymentTransactions.AsNoTracking()
            .Where(t => t.Status == PaymentTransactionStatus.Matched || t.Status == PaymentTransactionStatus.Resolved);
        if (!string.IsNullOrEmpty(filter.Environment))
        {
            string environment = filter.Environment;
            query = query.Where(t => t.Environment == environment);
        }
        if (filter.From != null)
        {
            DateTimeOffset from = filter.From.Value;
            query = query.Where(t => t.ReceivedAt >= from);
        }
        if (filter.ToExclusive != null)
        {
            DateTimeOffset to = filter.ToExclusive.Value;
            query = query.Where(t => t.ReceivedAt < to);
        }
        return await query
            .OrderBy(t => t.ReceivedAt)
            .Select(t => new RevenueTransaction { ReceivedAt = t.ReceivedAt, Amount = t.TransferAmount })
            .ToListAsync(ct);
    }

    public async Task<bool> TryAddAsync(PaymentTransaction transaction, CancellationToken ct)
    {
        context.PaymentTransactions.Add(transaction);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (DbExceptions.IsUniqueViolation(exception))
        {
            DbExceptions.DetachAll(context, transaction);
            return false;
        }
    }
}
