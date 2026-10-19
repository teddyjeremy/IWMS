# IWMS

Inventory and warehouse management system built with .NET 10, ASP.NET Core, Entity Framework Core and PostgreSQL.

## Projects

- **IWMS.Api** — ASP.NET Core API, authentication, authorization and HTTP endpoints.
- **IWMS.Desktop** — WPF Windows client for dashboard, customer/order entry, fulfillment actions and reports.
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

Find both artifacts under **Actions → Build and test → workflow run → Artifacts**: `IWMS-Api-win-x64` and `IWMS-Desktop-win-x64`. The API needs valid JWT/database configuration and a reachable PostgreSQL database; the desktop client needs the API running and reachable.

Architecture and entity relationship diagrams are in [`docs/architecture.md`](docs/architecture.md).

## Development roadmap

Work in small, reviewable sections. Keep each commit focused, run the build and tests, and use the Actions result as the checkpoint before moving to the next section.

1. Validate the existing project references, migrations, receiving, stock ledger and purchasing flows with a successful CI run.
2. Exercise fulfillment end to end: reservations, partial stock, backorders, pick lists, dispatch, and cancellation under concurrent requests.
3. Validate dashboard totals, trend data and CSV/PDF exports against seeded database scenarios.
4. Stock transfers now support approval, dispatch, receipt and discrepancy notes; return records cover customer and supplier returns; stock-count sessions support snapshot, physical-count entry, variance reasons and transactional adjustment posting.
5. Finish desktop editing and administrative screens, improve validation/error presentation, and test the packaged app against a configured API.
6. Expand automated tests and add release/versioning and deployment guidance.

The Windows desktop client lives in `IWMS.Desktop` (WPF). GitHub Actions publishes it separately as `IWMS-Desktop-win-x64`. The current client includes sign-in, a live KPI dashboard, API-backed read views for customers, sales orders, products, purchase orders, stock transfers, stock counts and returns; customer and sales-order creation; order confirmation/cancellation; reservation, pick-list, picking, dispatch and short-close actions; transfer request/approval/rejection/dispatch/receipt; stock-count entry/posting; basic customer/supplier return entry; and inventory CSV/PDF exports. Editing existing customer/product/order details and broader administrative screens remain in progress.

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
- `POST /api/fulfillment/sales-orders/{id}/close-short` — close an eligible backordered/partially fulfilled order, release remaining reservations and cancel open pick lists.
- `GET /api/stock-transfers` and `GET /api/stock-transfers/{id}` — list transfer documents and inspect line-level quantities and status.
- `POST /api/stock-transfers` — create a warehouse transfer request with source/destination locations and quantities; it enters PendingApproval.
- `POST /api/stock-transfers/{id}/approve` and `POST /api/stock-transfers/{id}/reject` — decide a pending transfer.
- `POST /api/stock-transfers/{id}/dispatch` — deduct available stock at source locations and post transfer-out movements.
- `POST /api/stock-transfers/{id}/receive` — receive all lines, post transfer-in movements for accepted quantities, and require notes for discrepancies.
- `GET /api/returns` and `GET /api/returns/{id}` — inspect customer and supplier return records.
- `POST /api/returns` — process a customer return against a dispatched sales order or a supplier return against a goods receipt. Restocked customer returns and supplier returns post stock movements; quarantine and scrap dispositions are recorded without increasing available stock.
- `GET /api/stock-counts` and `GET /api/stock-counts/{id}` — inspect stock-count sessions and line-level system/count quantities.\n- `POST /api/stock-counts` — start a count for a warehouse.\n- `PUT /api/stock-counts/{id}/lines` — record physical quantities and variance reasons; the session moves to review when every line is counted.\n- `POST /api/stock-counts/{id}/post` — post stock adjustments for variances; posting is stopped if inventory changed after the count snapshot.\n- `GET /api/reports/dashboard` — stock value, low-stock products, open purchase orders, pending approvals and order totals.
- `GET /api/reports/inventory-trend?days=30` — daily movement totals for the requested period.
- `GET /api/reports/inventory.csv` — download an inventory CSV.
- `GET /api/reports/inventory.pdf` — download a printable inventory PDF.

The desktop client is a WPF app in `IWMS.Desktop`. It connects to the API URL entered at sign-in; configure the API's database and JWT settings before use. Download `IWMS-Desktop-win-x64` and `IWMS-Api-win-x64` from the same successful GitHub Actions run.

Fulfillment operations that modify stock use serializable database transactions. The latest GitHub Actions run passed the solution build and tests and successfully published both Windows artifacts. This verifies compilation and packaging, not live-database behaviour; test workflows against a non-production database before operational use. The API and desktop client are published as separate artifacts; the API artifact is not a desktop installer.

The desktop transfer and return forms are intentionally simple operator dialogs and currently create one line per document; the API accepts multi-line documents. The desktop transfer receipt form supports partial quantities and requires a note for discrepancies. Quarantine and scrap returns are recorded as dispositions but do not increase available inventory; quarantine stock is not separately balance-tracked yet.

## Security

Never commit database passwords, JWT signing keys, production credentials or real customer data.
