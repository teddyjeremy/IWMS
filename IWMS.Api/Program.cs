using IWMS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using IWMS.Infrastructure.Seeding;
using IWMS.Application;
using IWMS.Application.Receiving;
var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<GoodsReceivingService>();


// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();

    var adminPassword = builder.Configuration["Seed:AdminPassword"]
        ?? throw new InvalidOperationException("Seed:AdminPassword is missing from user secrets.");
    await DataSeeder.SeedAsync(db, adminPassword);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
