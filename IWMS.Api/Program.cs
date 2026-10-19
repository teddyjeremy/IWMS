using System.Text;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using IWMS.Api.Errors;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using IWMS.Application.Ledger;
using IWMS.Application.Receiving;
using IWMS.Domain.Entities;
using IWMS.Domain.Common;
using IWMS.Infrastructure;
using IWMS.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});

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

// Optional one-time production administrator provisioning. Configure these values as
// host secrets, never in source control. Existing accounts are never overwritten.
var bootstrapAdminEmail = builder.Configuration["BootstrapAdmin:Email"];
var bootstrapAdminPassword = builder.Configuration["BootstrapAdmin:Password"];
if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail) ||
    !string.IsNullOrWhiteSpace(bootstrapAdminPassword))
{
    if (string.IsNullOrWhiteSpace(bootstrapAdminEmail) ||
        string.IsNullOrWhiteSpace(bootstrapAdminPassword))
        throw new InvalidOperationException("Both BootstrapAdmin:Email and BootstrapAdmin:Password must be configured together.");

    if (bootstrapAdminPassword.Length < 10 ||
        !bootstrapAdminPassword.Any(char.IsLetter) ||
        !bootstrapAdminPassword.Any(char.IsDigit))
        throw new InvalidOperationException("BootstrapAdmin:Password must be at least 10 characters and contain letters and digits.");

    using var bootstrapScope = app.Services.CreateScope();
    var bootstrapDb = bootstrapScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var normalizedBootstrapEmail = bootstrapAdminEmail.Trim().ToLowerInvariant();
    var existingBootstrapUser = await bootstrapDb.Users
        .Include(u => u.Roles)
        .SingleOrDefaultAsync(u => u.Email == normalizedBootstrapEmail);

    if (existingBootstrapUser is null)
    {
        var administratorRole = await bootstrapDb.Roles
            .SingleOrDefaultAsync(r => r.Name == RoleNames.Administrator)
            ?? throw new InvalidOperationException("The Administrator role is missing. Apply migrations and initialize IWMS roles before bootstrapping an administrator.");

        var name = builder.Configuration["BootstrapAdmin:Name"]?.Trim();
        var nameParts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = nameParts.FirstOrDefault() ?? "IWMS";
        var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : "Administrator";
        var newAdmin = new User
        {
            FirstName = firstName,
            LastName = lastName,
            Email = normalizedBootstrapEmail,
            Roles = { administratorRole }
        };
        var passwordHasher = bootstrapScope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        newAdmin.PasswordHash = passwordHasher.HashPassword(newAdmin, bootstrapAdminPassword);
        bootstrapDb.Users.Add(newAdmin);
        await bootstrapDb.SaveChangesAsync();
        app.Logger.LogInformation("Initial IWMS administrator provisioned for {AdminEmail}.", normalizedBootstrapEmail);
    }
    else
    {
        app.Logger.LogInformation("Bootstrap administrator already exists; no account changes were made.");
    }
}


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

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();