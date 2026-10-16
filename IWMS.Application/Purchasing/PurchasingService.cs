using IWMS.Application.Audit;
using IWMS.Application.Common;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application.Purchasing;

public class PurchasingService
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public PurchasingService(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PurchaseOrderCreated> CreateAsync(
        SavePurchaseOrderRequest request, Guid userId, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);

        var number = await _db.NextPurchaseOrderNumberAsync(ct);

        var order = new PurchaseOrder
        {
            OrderNumber = $"PO-{DateTime.UtcNow.Year}-{number:D5}",
            Status = PurchaseOrderStatus.Draft,
            SupplierId = request.SupplierId,
            CreatedByUserId = userId,
            ExpectedDeliveryUtc = request.ExpectedDeliveryUtc?.AsUtc(),
            Notes = request.Notes?.Trim() ?? string.Empty
        };

        foreach (var input in request.Lines)
            order.Lines.Add(NewLine(input));

        _db.PurchaseOrders.Add(order);
        _audit.Record(
            "PurchaseOrder.Created", nameof(PurchaseOrder), order.Id, userId,
            details: $"{order.OrderNumber}, {request.Lines.Count} line(s)");

        await _db.SaveChangesAsync(ct);

        return new PurchaseOrderCreated(order.Id, order.OrderNumber);
    }

    public async Task UpdateAsync(
        Guid orderId, SavePurchaseOrderRequest request, Guid userId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);

        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Rejected))
            throw new DomainException(
                $"Only a draft or rejected order can be edited (current status: {order.Status}).");

        await ValidateAsync(request, ct);

        _db.SetOriginalVersion(order, request.Version);

        _db.PurchaseOrderLines.RemoveRange(order.Lines.ToList());

        foreach (var input in request.Lines)
        {
            var line = NewLine(input);
            line.PurchaseOrderId = order.Id;
            _db.PurchaseOrderLines.Add(line);
        }

        order.SupplierId = request.SupplierId;
        order.ExpectedDeliveryUtc = request.ExpectedDeliveryUtc?.AsUtc();
        order.Notes = request.Notes?.Trim() ?? string.Empty;
        order.Status = PurchaseOrderStatus.Draft;
        order.RejectionReason = null;
        order.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(
            "PurchaseOrder.Updated", nameof(PurchaseOrder), order.Id, userId,
            details: $"{order.OrderNumber}, {request.Lines.Count} line(s)");

        await _db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(Guid orderId, Guid userId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);

        if (order.Status != PurchaseOrderStatus.Draft)
            throw new DomainException(
                $"Only a draft order can be submitted (current status: {order.Status}).");
        if (order.Lines.Count == 0)
            throw new DomainException("An order without lines cannot be submitted.");

        order.Status = PurchaseOrderStatus.Submitted;
        order.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(
            "PurchaseOrder.Submitted", nameof(PurchaseOrder), order.Id, userId,
            details: order.OrderNumber);

        await _db.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(Guid orderId, Guid approverId, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);

        if (order.Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(
                $"Only a submitted order can be approved (current status: {order.Status}).");
        if (order.CreatedByUserId == approverId)
            throw new DomainException("The person who created an order cannot approve it.");

        order.Status = PurchaseOrderStatus.Approved;
        order.ApprovedByUserId = approverId;
        order.ApprovedAtUtc = DateTime.UtcNow;
        order.RejectionReason = null;
        order.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(
            "PurchaseOrder.Approved", nameof(PurchaseOrder), order.Id, approverId,
            details: order.OrderNumber);

        await _db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(
        Guid orderId, string reason, Guid approverId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection requires a reason.");

        var order = await LoadAsync(orderId, ct);

        if (order.Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(
                $"Only a submitted order can be rejected (current status: {order.Status}).");

        order.Status = PurchaseOrderStatus.Rejected;
        order.RejectionReason = reason.Trim();
        order.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(
            "PurchaseOrder.Rejected", nameof(PurchaseOrder), order.Id, approverId,
            reason.Trim(), order.OrderNumber);

        await _db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(
        Guid orderId, string reason, Guid userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation requires a reason.");

        var order = await LoadAsync(orderId, ct);

        if (order.Status is PurchaseOrderStatus.Cancelled
            or PurchaseOrderStatus.PartiallyReceived
            or PurchaseOrderStatus.Received)
            throw new DomainException($"An order in status {order.Status} cannot be cancelled.");

        if (order.Lines.Any(l => l.QuantityReceived > 0))
            throw new DomainException("This order has received goods. Reverse the receipts first.");

        order.Status = PurchaseOrderStatus.Cancelled;
        order.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(
            "PurchaseOrder.Cancelled", nameof(PurchaseOrder), order.Id, userId,
            reason.Trim(), order.OrderNumber);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<PurchaseOrder> LoadAsync(Guid orderId, CancellationToken ct)
    {
        return await _db.PurchaseOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new DomainException("Purchase order not found.");
    }

    private async Task ValidateAsync(SavePurchaseOrderRequest request, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("A purchase order needs at least one line.");

        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId && s.IsActive, ct))
            throw new DomainException("Supplier not found or inactive.");

        if (request.Lines.Any(l => l.Quantity <= 0))
            throw new DomainException("Every line quantity must be greater than zero.");
        if (request.Lines.Any(l => l.ExpectedUnitPrice < 0))
            throw new DomainException("Expected prices cannot be negative.");

        var productIds = request.Lines.Select(l => l.ProductId).ToList();
        if (productIds.Distinct().Count() != productIds.Count)
            throw new DomainException("Each product can appear only once on an order.");

        var activeProducts = await _db.Products
            .CountAsync(p => productIds.Contains(p.Id) && p.IsActive, ct);
        if (activeProducts != productIds.Count)
            throw new DomainException("One or more products were not found or are inactive.");
    }

    private static PurchaseOrderLine NewLine(PurchaseOrderLineInput input) =>
        new()
        {
            ProductId = input.ProductId,
            QuantityOrdered = input.Quantity,
            ExpectedUnitPrice = input.ExpectedUnitPrice
        };
}