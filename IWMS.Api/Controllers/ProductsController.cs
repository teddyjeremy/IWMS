
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
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public ProductsController(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    // GET: api/products
    [HttpGet]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult<PagedResult<ProductListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1)
            throw new DomainException("Page must be at least 1.");

        if (pageSize < 1 || pageSize > 100)
            throw new DomainException(
                "Page size must be between 1 and 100.");

        var query = _db.Products.AsNoTracking();

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(p =>
                p.Sku.ToLower().Contains(term) ||
                p.Name.ToLower().Contains(term) ||
                (p.Barcode != null &&
                 p.Barcode.ToLower().Contains(term)));
        }

        var result = await query
            .OrderBy(p => p.Sku)
            .Select(p => new ProductListItem(
                p.Id,
                p.Sku,
                p.Name,
                p.Category.Name,
                p.BaseUnit,
                p.Balances.Sum(b => (decimal?)b.OnHand) ?? 0m,
                p.Balances.Sum(b => (decimal?)b.Reserved) ?? 0m,
                p.Balances.Sum(
                    b => (decimal?)(b.OnHand - b.Reserved)) ?? 0m,
                p.ReorderLevel ?? 0m,
                (p.Balances.Sum(
                    b => (decimal?)(b.OnHand - b.Reserved)) ?? 0m)
                    <= (p.ReorderLevel ?? 0m),
                p.IsActive))
            .ToPagedAsync(page, pageSize, ct);

        return Ok(result);
    }

    // GET: api/products/{id}
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult<ProductDetail>> Get(
        Guid id,
        CancellationToken ct)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDetail(
                p.Id,
                p.Sku,
                p.Name,
                p.Description,
                p.Barcode,
                p.CategoryId,
                p.Category.Name,
                p.BaseUnit,
                p.CostPrice,
                p.ReorderLevel ?? 0m,
                p.IsActive,
                p.Balances
                    .OrderBy(b => b.StorageLocation.Code)
                    .Select(b => new StockByLocation(
                        b.StorageLocationId,
                        b.StorageLocation.Warehouse.Name,
                        b.StorageLocation.Code,
                        b.OnHand,
                        b.Reserved,
                        b.OnHand - b.Reserved))
                    .ToList(),
                p.Version))
            .SingleOrDefaultAsync(ct);

        if (product is null)
            return NotFound();

        return Ok(product);
    }

    // POST: api/products
    [HttpPost]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult> Create(
        SaveProductRequest request,
        CancellationToken ct)
    {
        await ValidateAsync(request, existingId: null, ct);

        var product = new Product
        {
            Sku = request.Sku.Trim(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Barcode = string.IsNullOrWhiteSpace(request.Barcode)
                ? null
                : request.Barcode.Trim(),
            CategoryId = request.CategoryId,
            BaseUnit = request.BaseUnit,
            CostPrice = request.CostPrice,
            ReorderLevel = request.ReorderLevel,
            IsActive = request.IsActive
        };

        _db.Products.Add(product);

        await _db.SaveChangesAsync(ct);

        _audit.Record(
            "Product.Created",
            nameof(Product),
            product.Id,
            User.GetUserId(),
            details: $"SKU {product.Sku}");

        await _db.SaveChangesAsync(ct);

        return StatusCode(
            StatusCodes.Status201Created,
            new { product.Id });
    }

    // PUT: api/products/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult> Update(
        Guid id,
        SaveProductRequest request,
        CancellationToken ct)
    {
        var product = await _db.Products
            .SingleOrDefaultAsync(p => p.Id == id, ct);

        if (product is null)
            return NotFound();

        // Use the version supplied by the client for concurrency checking.
        _db.SetOriginalVersion(product, request.Version);

        await ValidateAsync(request, existingId: id, ct);

        product.Sku = request.Sku.Trim();
        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim() ?? string.Empty;
        product.Barcode = string.IsNullOrWhiteSpace(request.Barcode)
            ? null
            : request.Barcode.Trim();
        product.CategoryId = request.CategoryId;
        product.BaseUnit = request.BaseUnit;
        product.CostPrice = request.CostPrice;
        product.ReorderLevel = request.ReorderLevel;
        product.IsActive = request.IsActive;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _audit.Record(
            "Product.Updated",
            nameof(Product),
            product.Id,
            User.GetUserId(),
            details: $"SKU {product.Sku}");

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // Validate product data before creating or updating.
    private async Task ValidateAsync(
        SaveProductRequest request,
        Guid? existingId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
            throw new DomainException("SKU is required.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("Product name is required.");

        if (!Enum.IsDefined(request.BaseUnit))
            throw new DomainException("Unknown unit of measure.");

        if (request.ReorderLevel < 0)
            throw new DomainException(
                "Reorder level cannot be negative.");

        if (request.CostPrice < 0)
            throw new DomainException(
                "Cost price cannot be negative.");

        var sku = request.Sku.Trim();

        var skuExists = await _db.Products.AnyAsync(
            p => p.Sku == sku && p.Id != existingId,
            ct);

        if (skuExists)
            throw new DomainException(
                $"A product with SKU '{sku}' already exists.");

        var categoryExists = await _db.Categories.AnyAsync(
            c => c.Id == request.CategoryId,
            ct);

        if (!categoryExists)
            throw new DomainException("Category not found.");
    }
}
