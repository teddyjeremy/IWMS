using IWMS.Api.Dtos;
using IWMS.Application;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly IAppDbContext _db;

    public CategoriesController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoryItem>>> List(CancellationToken ct)
    {
        var items = await _db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryItem(c.Id, c.Name, c.Description))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult> Create(SaveCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("Category name is required.");

        var name = request.Name.Trim();
        if (await _db.Categories.AnyAsync(c => c.Name == name, ct))
            throw new DomainException("A category with this name already exists.");

        var category = new Category
        {
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created,
            new CategoryItem(category.Id, category.Name, category.Description));
    }
}