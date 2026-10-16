using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Dtos;

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<T>(items, page, pageSize, total);
    }
}