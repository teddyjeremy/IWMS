# IWMS — Inventory & Warehouse Management

IWMS answers one question for a growing distributor or workshop:

**What do we have, where is it, what is already promised, and why did the quantity change?**

It is a Windows desktop app plus an ASP.NET Core API. Stock is not a number you type over. Every receipt, issue, adjustment, transfer and dispatch is a posted movement. The balance you see is the result of those movements.

This repository is the portfolio build of that system. The [project blueprint](docs/architecture.md) describes the full product. This README says what you can do today, who it is for, and how to run it.

## Who it is for

| If you are… | You spend your day… | IWMS lets you… |
| --- | --- | --- |
| Warehouse manager | Knowing what is short, what is waiting, and what does not add up | See the dashboard, approve purchase orders and transfers, review counts, and read the audit trail |
| Purchaser | Getting stock in from suppliers | Create a supplier, raise a purchase order, submit it, and follow receipts |
| Receiver | Checking what actually arrived | Receive part of an order now and the rest later, and note discrepancies |
| Sales officer | Promising stock to a customer | Enter a customer and a sales order, then confirm or cancel it while it is still a draft |
| Warehouse operator | Picking, moving and counting | Reserve, pick and dispatch orders, transfer stock, count a warehouse, and post returns |
| Viewer / auditor | Explaining a number after the fact | Read inventory, movements, reports and who changed what |
| Administrator | Keeping the team in the system | Create users, assign one role, reset a password, and deactivate an account |

The sidebar only shows the workspaces for your role. The API still refuses an action your role is not allowed to take, even if a button were visible.

Local demo accounts (Development seed only, all using the `Seed:AdminPassword` you set):

| Person | Email | Role |
| --- | --- | --- |
| System Administrator | `admin@iwms.local` | Administrator |
| Mary Manager | `manager@iwms.local` | Warehouse Manager |
| Paul Purchaser | `purchaser@iwms.local` | Purchaser |
| Olivia Operator | `operator@iwms.local` | Warehouse Operator |
| Sam Sales | `sales@iwms.local` | Sales Officer |
| Vera Viewer | `viewer@iwms.local` | Viewer |

Do not reuse these addresses or that password outside a local database.

## A normal day in the system

This is the path a reviewer can follow.

1. **Buy it.** Paul creates a purchase order and submits it. Mary approves it. The person who created the order cannot approve their own order.
2. **Receive it.** Olivia receives part of the order against the outstanding quantity, then receives the remainder later. Each receipt posts stock into a bin. A mistake is reversed with a reason. The original receipt is not edited.
3. **See it.** Inventory shows on-hand, reserved and available stock. Available is on-hand minus reserved. Low stock and reorder level are visible without opening a raw identifier.
4. **Promise it.** Sam enters a customer and a draft sales order, then confirms it. Confirming a draft does not, by itself, take stock off the shelf.
5. **Pick it.** Olivia reserves eligible stock, builds a pick list, records what was picked, and dispatches the shipment. The ledger shows the issue, who did it, and the quantity before and after.
6. **Move or count it.** A transfer asks for approval, leaves the source bin, and is received at the destination. A short receipt needs a note. A stock count is blind: the counter does not see the book quantity until the variance review.
7. **Explain it.** Movements, receipts and the audit trail name the person. Products and sales orders show who entered them. Reports cover stock value, dead stock, ABC class and supplier fill rate.

If a button cannot do the job, the status bar says why. A draft can be confirmed. A submitted purchase order can be approved or rejected. A reserved sales order is cancelled through fulfillment so the reservation is released. A dispatched order stays in history.

## What this version includes

**In the product now**

- Product catalogue: SKU, name, category, unit, barcode, reorder level, cost, active flag, and optional lot or serial tracking.
- Warehouses and bins.
- Immutable stock ledger and current balances (on-hand, reserved, available).
- Suppliers, purchase orders (draft → submitted → approved → partially received → received, plus reject and cancel), and partial goods receipts.
- Customers and sales orders (draft, confirm, edit while still a draft, cancel).
- Reservation, pick lists, dispatch, close-short, and backorder handling.
- Transfers with approval, dispatch and receipt, including a note when the quantity does not match.
- Customer and supplier returns. Restock posts stock back. Quarantine and scrap are recorded and do **not** increase available stock. Quarantine is not a separate balance yet.
- Cycle counts with a hidden book quantity, variance reasons, and posting.
- Lot / expiry suggestion (first expiry, first out) and serial capture where a product is marked to track them.
- Alerts for low stock, purchase orders waiting for approval, and overdue deliveries.
- Dashboard, movement chart, CSV and PDF export, purchase-order PDF and delivery note.
- CSV import for products and opening stock.
- Users, roles, and an audit trail.
- Search on the main lists. Identifier columns stay in the data for actions, but they are not shown in the grid.

**Still on the blueprint, not in this build**

- Product images, brands, variants, and unit conversions (each / pack / carton).
- Email or push notifications. Alerts are inside the app.
- A separate quarantine location balance.
- Camera scanning. A USB scanner works as keyboard input into the barcode box.
- Live updates (SignalR), credit limits, more than one company, offline scanning, and demand forecasting.

Those stay out until the receive → reserve → dispatch path is the one people trust.

## Rules the software will not break

- A posted movement is never edited. Corrections are reversals.
- You cannot reserve or dispatch more than the quantity the rules allow.
- Receiving more than the outstanding purchase-order quantity is rejected unless the receiving rules explicitly allow it.
- Money and quantities use decimals, not floating-point numbers.
- Times are stored in UTC and shown in local time.
- Two people cannot silently overwrite the same order. Updates carry a version.
- Master records are deactivated, not deleted, when history must remain.
- Approval, adjustment, receiving and user administration are checked on the API, not only hidden in the menu.

## Get the Windows app

GitHub Actions builds the solution on every push to `master`. After tests pass it publishes two self-contained Windows x64 packages.

Open **Actions → Build and test → the latest green run → Artifacts**:

- `IWMS-Desktop-win-x64` — `IWMS.Desktop.exe`. This is the app you double-click. It does not need a separate .NET install.
- `IWMS-Api-win-x64` — the API, only if you are hosting it yourself. It is not the desktop program.

Sign in with a user that already exists on the API the app is pointed at. For a managed install, set `IWMS_API_BASE_URL` before starting the desktop app. Remote API addresses must be HTTPS. `http://` is accepted only for localhost.

## Run it on your machine

You need the .NET 10 SDK, PostgreSQL, and Git.

```powershell
dotnet restore IWMS.slnx
dotnet build IWMS.slnx --configuration Release
dotnet test IWMS.slnx --configuration Release
```

Put secrets in user-secrets, not in the repository:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=iwms;Username=YOUR_USER;Password=YOUR_PASSWORD" --project IWMS.Api
dotnet user-secrets set "Jwt:Key" "REPLACE_WITH_A_LONG_RANDOM_SECRET_KEY" --project IWMS.Api
dotnet user-secrets set "Jwt:Issuer" "IWMS" --project IWMS.Api
dotnet user-secrets set "Jwt:Audience" "IWMS" --project IWMS.Api
dotnet user-secrets set "Seed:AdminPassword" "REPLACE_WITH_A_STRONG_PASSWORD" --project IWMS.Api
dotnet run --project IWMS.Api
```

In Development the API applies EF Core migrations and seeds the demo people above. It does **not** do that automatically outside Development. Read [production deployment](docs/production-deployment.md) before pointing this at a shared database.

## How the code is split

| Project | What lives there |
| --- | --- |
| `IWMS.Desktop` | WPF client: sign-in, sidebar, lists, forms, charts |
| `IWMS.Api` | HTTP API, sign-in, role policies, PDF and CSV |
| `IWMS.Application` | Purchasing, receiving, the stock ledger, lot allocation |
| `IWMS.Domain` | Products, orders, movements, and the rules that do not depend on a database |
| `IWMS.Infrastructure` | EF Core, PostgreSQL, migrations, demo seed |
| `IWMS.Tests` | Stock maths, purchase-order lines, sales-order permissions, FEFO allocation |

`InventoryMovement` is the history. `InventoryBalance` is the current shelf quantity, updated in the same transaction. If those two ever disagree, the movement history is what you investigate.

More detail: [architecture](docs/architecture.md), [roles](docs/role-based-workspaces.md), [production hosting](docs/production-deployment.md).

## Security

Never commit database passwords, JWT signing keys, production credentials, or a real customer list.
