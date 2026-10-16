using IWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application.Common;

public static class InventoryBalanceQueries
{
    public static async Task<InventoryBalance?> FindBalanceAsync(
        this IAppDbContext db, Guid productId, Guid storageLocationId, CancellationToken ct)
    {
        return db.InventoryBalances.Local
                   .FirstOrDefault(b => b.ProductId == productId && b.StorageLocationId == storageLocationId)
               ?? await db.InventoryBalances
                   .FirstOrDefaultAsync(b => b.ProductId == productId && b.StorageLocationId == storageLocationId, ct);
    }

    public static async Task<InventoryBalance> GetOrCreateBalanceAsync(
        this IAppDbContext db, Guid productId, Guid storageLocationId, CancellationToken ct)
    {
        var balance = await db.FindBalanceAsync(productId, storageLocationId, ct);

        if (balance is null)
        {
            balance = new InventoryBalance
            {
                ProductId = productId,
                StorageLocationId = storageLocationId
            };
            db.InventoryBalances.Add(balance);
        }

        return balance;
    }
}