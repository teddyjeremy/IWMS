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

GitHub Actions runs on pushes and pull requests targeting `master` or `main`. It restores, builds and tests the solution on Windows. After those checks pass, it publishes both the API and WPF desktop client for Windows x64 as self-contained deployments and uploads separate workflow artifacts.

Find the result under **Actions → Build and test → workflow run → Artifacts**. This artifact is the API deployment output, not a complete Windows desktop UI installer. A self-contained publish still needs valid configuration and a reachable PostgreSQL database to run.

## Development roadmap

Work in small, reviewable sections. Keep each commit focused, run the build and tests, and use the Actions result as the checkpoint before moving to the next section.

1. Stabilise project references, persistence and the existing receiving, stock ledger and purchasing flows.
2. Complete fulfillment: customers, sales orders, stock reservations, picking, dispatch, backorders and cancellation.
3. Add reporting: stock value, low-stock items, open purchase orders, pending approvals, trend data and CSV/PDF export.
4. Add document-based transfers, stock counts, returns and short-closing orders.
5. Build the Windows desktop UI against the API, then package the UI and its required runtime/deployment files.
6. Add CI coverage, setup instructions, architecture and entity-relationship diagrams, and release documentation.

The Windows desktop client lives in `IWMS.Desktop` (WPF). GitHub Actions publishes it separately as `IWMS-Desktop-win-x64`. The current client includes sign-in, a live KPI dashboard, API-backed read views for customers, sales orders, products and purchase orders, customer and sales-order creation, order confirmation/cancellation, reservation and pick-list actions, shipment picking/dispatch/cancellation, and inventory CSV/PDF exports. Editing existing customer/product/order details and broader administrative screens remain in progress.

## Fulfillment and reporting endpoints

All endpoints below require a valid bearer token unless the authentication endpoint explicitly allows anonymous access.

- `POST /api/auth/login` — obtain a bearer token.
- `GET /api/customers` and `GET /api/customers/{id}` — list/search and inspect customers.
- `POST /api/customers`, `PUT /api/customers/{id}`, `POST /api/customers/{id}/deactivate` — maintain customer records.
- `GET /api/sales-orders` and `GET /api/sales-orders/{id}` — inspect orders.
- `POST /api/sales-orders`, `POST /api/sales-orders/{id}/confirm`, `POST /api/sales-orders/{id}/cancel` — create and manage eligible orders.
- `POST /api/fulfillment/sales-orders/{id}/reserve` — reserve available stock and report shortages.
- `POST /api/fulfillment/sales-orders/{id}/pick-list` — create pick lists from active reservations.
- `POST /api/fulfillment/shipments/{id}/pick` — record picked quantities.
- `POST /api/fulfillment/shipments/{id}/dispatch` — post dispatch movements and update order fulfillment.
- `POST /api/fulfillment/sales-orders/{id}/cancel` and `POST /api/fulfillment/shipments/{id}/cancel` — cancel eligible orders/shipments and release order reservations.
- `GET /api/reports/dashboard` — stock value, low-stock products, open purchase orders, pending approvals and order totals.
- `GET /api/reports/inventory-trend?days=30` — daily movement totals for the requested period.
- `GET /api/reports/inventory.csv` — download an inventory CSV.
- `GET /api/reports/inventory.pdf` — download a printable inventory PDF.

The desktop client is a WPF app in `IWMS.Desktop`. It connects to the API URL entered at sign-in; configure the API's database and JWT settings before use. Download `IWMS-Desktop-win-x64` and `IWMS-Api-win-x64` from the same successful GitHub Actions run.

Fulfillment operations that modify stock use a database transaction. Test these workflows against a non-production database before operational use. PDF reporting and the Windows desktop UI are still under development; the current CI publish artifact is the API, not a desktop installer.

## Security

Never commit database passwords, JWT signing keys, production credentials or real customer data.
