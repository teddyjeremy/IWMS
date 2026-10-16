using System.Text;
using System.Text.Json.Serialization;
using IWMS.Api.Errors;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using IWMS.Application.Ledger;
using IWMS.Application.Receiving;
using IWMS.Domain.Entities;
using IWMS.Infrastructure;
using IWMS.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using IWMS.Application.Purchasing;

var builder = WebApplication.CreateBuilder(args);


var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is missing from user secrets.");

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<GoodsReceivingService>();
builder.Services.AddScoped<StockLedgerService>();

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<StockReversalService>();
builder.Services.AddScoped<PurchasingService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<IAppDbContext>();
                var idText = context.Principal?.FindFirst("sub")?.Value;

                var stillActive = Guid.TryParse(idText, out var userId)
                    && await db.Users.AnyAsync(u => u.Id == userId && u.IsActive);

                if (!stillActive)
                    context.Fail("The user is no longer active.");
            }
        };
    });

builder.Services.AddIwmsAuthorization();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        var demoPassword = builder.Configuration["Seed:AdminPassword"]
            ?? throw new InvalidOperationException("Seed:AdminPassword is missing from user secrets.");
        await DataSeeder.SeedAsync(db, demoPassword);
    }

    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();