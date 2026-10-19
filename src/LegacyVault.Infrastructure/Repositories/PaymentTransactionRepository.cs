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
        PaymentTransactionStatus? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<PaymentTransaction> query = context.PaymentTransactions.AsNoTracking();
        if (status != null)
        {
            query = query.Where(t => t.Status == status.Value);
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
