using IWMS.Api.Dtos;
using IWMS.Application;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/warehouses")]
public class WarehousesController : ControllerBase
{
    private readonly IAppDbContext _db;

    public WarehousesController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<WarehouseItem>>> List(CancellationToken ct)
    {
        var items = await _db.Warehouses.AsNoTracking()
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseItem(
                w.Id,
                w.Code,
                w.Name,
                w.Address,
                w.IsActive,
                w.StorageLocations
                    .OrderBy(l => l.Code)
                    .Select(l => new LocationItem(l.Id, l.Code, l.Description, l.IsActive))
                    .ToList()))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreateWarehouseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new DomainException("Warehouse code is required.");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("Warehouse name is required.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Warehouses.AnyAsync(w => w.Code == code, ct))
            throw new DomainException($"A warehouse with code '{code}' already exists.");

        var warehouse = new Warehouse
        {
            Code = code,
            Name = request.Name.Trim(),
            Address = request.Address?.Trim() ?? string.Empty
        };

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new { warehouse.Id });
    }

    [HttpPost("{id:guid}/locations")]
    public async Task<ActionResult> AddLocation(
        Guid id, CreateLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new DomainException("Location code is required.");

        if (!await _db.Warehouses.AnyAsync(w => w.Id == id && w.IsActive, ct))
            return NotFound();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.StorageLocations.AnyAsync(l => l.WarehouseId == id && l.Code == code, ct))
            throw new DomainException($"Location '{code}' already exists in this warehouse.");

        var location = new StorageLocation
        {
            WarehouseId = id,
            Code = code,
            Description = request.Description?.Trim() ?? string.Empty
        };

        _db.StorageLocations.Add(location);
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created,
            new LocationItem(location.Id, location.Code, location.Description, location.IsActive));
    }
}