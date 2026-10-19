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
[Route("api/customers")]
[Authorize]
public sealed class CustomersController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public CustomersController(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult<IReadOnlyList<CustomerListItem>>> List(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _db.Customers.AsNoTracking();

        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term) ||
                c.Phone.ToLower().Contains(term));
        }

        var customers = await query
            .OrderBy(c => c.Name)
            .Select(c => new CustomerListItem(
                c.Id, c.Name, c.Email, c.Phone, c.IsActive))
            .ToListAsync(ct);

        return Ok(customers);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult<CustomerDetail>> Get(
        Guid id,
        CancellationToken ct)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetail(
                c.Id, c.Name, c.Email, c.Phone, c.Address, c.IsActive, c.Version))
            .SingleOrDefaultAsync(ct);

        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<object>> Create(
        SaveCustomerRequest request,
        CancellationToken ct)
    {
        Validate(request);

        var name = request.Name.Trim();
        var email = request.Email.Trim();
        if (await _db.Customers.AnyAsync(
                c => c.Name == name && c.Email == email, ct))
            throw new DomainException("A customer with this name and email already exists.");

        var customer = new Customer
        {
            Name = name,
            Email = email,
            Phone = request.Phone.Trim(),
            Address = request.Address.Trim(),
            IsActive = true
        };

        _db.Customers.Add(customer);
        _audit.Record("Customer.Created", nameof(Customer), customer.Id,
            User.GetUserId(), details: customer.Name);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = customer.Id },
            new { customer.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveCustomerRequest request,
        CancellationToken ct)
    {
        Validate(request);

        var customer = await _db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
            return NotFound();

        var name = request.Name.Trim();
        var email = request.Email.Trim();
        if (await _db.Customers.AnyAsync(
                c => c.Id != id && c.Name == name && c.Email == email, ct))
            throw new DomainException("A customer with this name and email already exists.");

        customer.Name = name;
        customer.Email = email;
        customer.Phone = request.Phone.Trim();
        customer.Address = request.Address.Trim();
        customer.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record("Customer.Updated", nameof(Customer), customer.Id,
            User.GetUserId(), details: customer.Name);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
            return NotFound();

        if (!customer.IsActive)
            return NoContent();

        customer.IsActive = false;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("Customer.Deactivated", nameof(Customer), customer.Id,
            User.GetUserId(), details: customer.Name);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static void Validate(SaveCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            throw new DomainException("Customer name is required and must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 256 ||
            !request.Email.Contains('@'))
            throw new DomainException("A valid customer email is required (maximum 256 characters).");
        if (request.Phone.Trim().Length > 50)
            throw new DomainException("Phone must be 50 characters or fewer.");
        if (request.Address.Trim().Length > 500)
            throw new DomainException("Address must be 500 characters or fewer.");
    }
}

public sealed record SaveCustomerRequest(
    string Name,
    string Email,
    string Phone = "",
    string Address = "");

public sealed record CustomerListItem(
    Guid Id,
    string Name,
    string Email,
    string Phone,
    bool IsActive);

public sealed record CustomerDetail(
    Guid Id,
    string Name,
    string Email,
    string Phone,
    string Address,
    bool IsActive,
    uint Version);
