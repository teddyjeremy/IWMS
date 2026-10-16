using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Policy = Policies.ViewData)]
public class SuppliersController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public SuppliersController(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.Suppliers.AsNoTracking();

        if (!includeInactive)
            query = query.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) || s.ContactPerson.ToLower().Contains(term));
        }

        var result = await query
            .OrderBy(s => s.Name)
            .Select(s => new SupplierListItem(
                s.Id, s.Name, s.ContactPerson, s.Email, s.Phone, s.IsActive))
            .ToPagedAsync(page, pageSize, ct);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDetail>> Get(Guid id, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SupplierDetail(
                s.Id, s.Name, s.ContactPerson, s.Email, s.Phone, s.Address, s.IsActive, s.Version))
            .SingleOrDefaultAsync(ct);

        if (supplier is null)
            return NotFound();

        return Ok(supplier);
    }

    [Authorize(Policy = Policies.ManageSuppliers)]
    [HttpPost]
    public async Task<ActionResult> Create(SaveSupplierRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, existingId: null, ct);

        var supplier = new Supplier
        {
            Name = request.Name.Trim(),
            ContactPerson = request.ContactPerson?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            Phone = request.Phone?.Trim() ?? string.Empty,
            Address = request.Address?.Trim() ?? string.Empty,
            IsActive = request.IsActive
        };

        _db.Suppliers.Add(supplier);
        _audit.Record("Supplier.Created", nameof(Supplier), supplier.Id, User.GetUserId(),
            details: supplier.Name);
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new { supplier.Id });
    }

    [Authorize(Policy = Policies.ManageSuppliers)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, SaveSupplierRequest request, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null)
            return NotFound();

        await ValidateAsync(request, existingId: id, ct);

        _db.SetOriginalVersion(supplier, request.Version);

        supplier.Name = request.Name.Trim();
        supplier.ContactPerson = request.ContactPerson?.Trim() ?? string.Empty;
        supplier.Email = request.Email?.Trim() ?? string.Empty;
        supplier.Phone = request.Phone?.Trim() ?? string.Empty;
        supplier.Address = request.Address?.Trim() ?? string.Empty;
        supplier.IsActive = request.IsActive;
        supplier.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record("Supplier.Updated", nameof(Supplier), supplier.Id, User.GetUserId(),
            details: supplier.Name);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task ValidateAsync(SaveSupplierRequest request, Guid? existingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("Supplier name is required.");

        var name = request.Name.Trim().ToLower();
        if (await _db.Suppliers.AnyAsync(s => s.Name.ToLower() == name && s.Id != existingId, ct))
            throw new DomainException("A supplier with this name already exists.");
    }
}