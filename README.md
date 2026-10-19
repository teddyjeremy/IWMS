# IWMS

Inventory and warehouse management system built with .NET 10, ASP.NET Core, Entity Framework Core and PostgreSQL.

## Projects

- **IWMS.Api** — ASP.NET Core API, authentication, authorization and HTTP endpoints.
- **IWMS.Application** — application services and use cases.
- **IWMS.Domain** — domain entities and enums.
- **IWMS.Infrastructure** — EF Core persistence, database configuration and seeding.
- **IWMS.Tests** — automated tests.

## Requirements

- .NET 10 SDK
- PostgreSQL
- Git

## Build and test

Run these commands from the repository root:

```powershell
dotnet restore IWMS.slnx
dotnet build IWMS.slnx --configuration Release
dotnet test IWMS.slnx --configuration Release
```

## Local configuration

The API expects configuration for its PostgreSQL connection and JWT authentication. Development startup also expects a seed administrator password. Keep secrets out of source control; use .NET user-secrets for local development.

From the repository root, configure secrets for the API project:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=iwms;Username=YOUR_USER;Password=YOUR_PASSWORD" --project IWMS.Api
dotnet user-secrets set "Jwt:Key" "REPLACE_WITH_A_LONG_RANDOM_SECRET_KEY" --project IWMS.Api
dotnet user-secrets set "Jwt:Issuer" "IWMS" --project IWMS.Api
dotnet user-secrets set "Jwt:Audience" "IWMS" --project IWMS.Api
dotnet user-secrets set "Seed:AdminPassword" "REPLACE_WITH_A_STRONG_PASSWORD" --project IWMS.Api
```

Then run:

```powershell
dotnet run --project IWMS.Api
```

Database migrations are applied automatically by the API only in the Development environment. Review migration and seeding behaviour before using a production database.

## Continuous integration and build artifacts

GitHub Actions runs on pushes and pull requests targeting `master` or `main`. It restores, builds and tests the solution on Windows. After those checks pass, it publishes the API for Windows x64 as a self-contained single-file deployment and uploads the publish output as a workflow artifact.

Find the result under **Actions → Build and test → workflow run → Artifacts**. This artifact is the API deployment output, not a complete Windows desktop UI installer. A self-contained publish still needs valid configuration and a reachable PostgreSQL database to run.

## Development roadmap

Work in small, reviewable sections. Keep each commit focused, run the build and tests, and use the Actions result as the checkpoint before moving to the next section.

1. Stabilise project references, persistence and the existing receiving, stock ledger and purchasing flows.
2. Complete fulfillment: customers, sales orders, stock reservations, picking, dispatch, backorders and cancellation.
3. Add reporting: stock value, low-stock items, open purchase orders, pending approvals, trend data and CSV/PDF export.
4. Add document-based transfers, stock counts, returns and short-closing orders.
5. Build the Windows desktop UI against the API, then package the UI and its required runtime/deployment files.
6. Add CI coverage, setup instructions, architecture and entity-relationship diagrams, and release documentation.

The Windows desktop application is a separate deliverable from the API publish. Choose and add a desktop framework (for example, WPF) as a dedicated project before claiming the desktop UI is packaged.

## Security

Never commit database passwords, JWT signing keys, production credentials or real customer data.
