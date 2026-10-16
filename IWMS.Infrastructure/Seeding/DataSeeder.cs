using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Infrastructure.Seeding;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, string adminPassword)
    {
        if (!await db.Roles.AnyAsync())
        {
            db.Roles.AddRange(
                new Role { Name = "Administrator", Description = "Manages users, roles, settings and master data." },
                new Role { Name = "Warehouse Manager", Description = "Oversees stock and approves adjustments and transfers." },
                new Role { Name = "Purchaser", Description = "Maintains suppliers and creates purchase orders." },
                new Role { Name = "Warehouse Operator", Description = "Receives, counts, picks and moves stock." },
                new Role { Name = "Sales Officer", Description = "Creates sales orders and tracks fulfillment." },
                new Role { Name = "Viewer", Description = "Read-only access to reports and records." });
            await db.SaveChangesAsync();
        }

        var demoUsers = new[]
{
            (First: "System", Last: "Administrator", Email: "admin@iwms.local", Role: "Administrator"),
            (First: "Mary", Last: "Manager", Email: "manager@iwms.local", Role: "Warehouse Manager"),
            (First: "Paul", Last: "Purchaser", Email: "purchaser@iwms.local", Role: "Purchaser"),
            (First: "Olivia", Last: "Operator", Email: "operator@iwms.local", Role: "Warehouse Operator"),
            (First: "Sam", Last: "Sales", Email: "sales@iwms.local", Role: "Sales Officer"),
            (First: "Vera", Last: "Viewer", Email: "viewer@iwms.local", Role: "Viewer")
        };

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name);
        var hasher = new PasswordHasher<User>();

        foreach (var demo in demoUsers)
        {
            if (await db.Users.AnyAsync(u => u.Email == demo.Email))
                continue;

            var user = new User
            {
                FirstName = demo.First,
                LastName = demo.Last,
                Email = demo.Email,
                Roles = { roles[demo.Role] }
            };
            user.PasswordHash = hasher.HashPassword(user, adminPassword);
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();

        if (!await db.Products.AnyAsync())
        {
            var tools = new Category { Name = "Hand Tools", Description = "Manual tools." };
            var fasteners = new Category { Name = "Fasteners", Description = "Screws, nails and bolts." };

            db.Products.AddRange(
                new Product
                {
                    Sku = "HAM-001",
                    Name = "Claw Hammer 16oz",
                    Category = tools,
                    BaseUnit = UnitOfMeasure.Each,
                    CostPrice = 8.50m,
                    ReorderLevel = 10
                },
                new Product
                {
                    Sku = "SCR-100",
                    Name = "Wood Screws 4x40mm (box of 100)",
                    Category = fasteners,
                    BaseUnit = UnitOfMeasure.Box,
                    CostPrice = 3.20m,
                    ReorderLevel = 25
                });

            db.Suppliers.Add(new Supplier
            {
                Name = "Acme Tools Ltd",
                ContactPerson = "Jane Doe",
                Email = "sales@acme.example",
                Phone = "+000 000 0000",
                Address = "1 Supplier Road"
            });

            db.Customers.Add(new Customer
            {
                Name = "Sample Customer Co",
                Email = "orders@customer.example",
                Phone = "+000 000 0001",
                Address = "2 Customer Street"
            });

            db.Warehouses.Add(new Warehouse
            {
                Code = "MAIN",
                Name = "Main Warehouse",
                Address = "Head office site",
                StorageLocations =
                {
                    new StorageLocation { Code = "A-01", Description = "Aisle A, rack 1" },
                    new StorageLocation { Code = "A-02", Description = "Aisle A, rack 2" },
                    new StorageLocation { Code = "B-01", Description = "Aisle B, rack 1" }
                }
            });

            await db.SaveChangesAsync();
        }
        if (!await db.PurchaseOrders.AnyAsync())
        {
            var supplier = await db.Suppliers.FirstAsync();
            var hammer = await db.Products.SingleAsync(p => p.Sku == "HAM-001");
            var purchaser = await db.Users.SingleAsync(u => u.Email == "purchaser@iwms.local");
            var manager = await db.Users.SingleAsync(u => u.Email == "manager@iwms.local");

            db.PurchaseOrders.Add(new PurchaseOrder
            {
                OrderNumber = "PO-0001",
                Status = PurchaseOrderStatus.Approved,
                Notes = "Demo order for the receiving walkthrough.",
                ExpectedDeliveryUtc = DateTime.UtcNow.AddDays(7),
                ApprovedAtUtc = DateTime.UtcNow,
                Supplier = supplier,
                CreatedByUser = purchaser,
                ApprovedByUser = manager,
                Lines =
                {
                    new PurchaseOrderLine
                    {
                        Product = hammer,
                        QuantityOrdered = 100,
                        ExpectedUnitPrice = 8.50m
                    }
                }
            });

            await db.SaveChangesAsync();
        }
    }
}