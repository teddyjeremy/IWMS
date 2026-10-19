# IWMS architecture

## Request flow

```mermaid
flowchart TD
    Desktop[WPF desktop client] -->|Bearer token / JSON| API[ASP.NET Core API]
    API --> App[Application services]
    App --> Domain[Domain entities and rules]
    API --> Infra[Infrastructure / EF Core]
    Infra --> DB[(PostgreSQL)]
    API --> Audit[Audit events]
    Audit --> DB
    CI[GitHub Actions] --> Build[Restore, build and test]
    Build --> APIArtifact[Windows x64 API artifact]
    Build --> DesktopArtifact[Windows x64 desktop artifact]
```

## Main inventory relationships

```mermaid
erDiagram
    CUSTOMER ||--o{ SALES_ORDER : places
    SALES_ORDER ||--|{ SALES_ORDER_LINE : contains
    PRODUCT ||--o{ SALES_ORDER_LINE : ordered
    SALES_ORDER_LINE ||--o{ RESERVATION : reserves
    STORAGE_LOCATION ||--o{ RESERVATION : holds
    SALES_ORDER ||--o{ SHIPMENT : fulfilled_by
    SHIPMENT ||--|{ SHIPMENT_LINE : contains
    SALES_ORDER_LINE ||--o{ SHIPMENT_LINE : picked_for
    STORAGE_LOCATION ||--o{ SHIPMENT_LINE : picked_from
    PRODUCT ||--o{ INVENTORY_BALANCE : stocked_as
    STORAGE_LOCATION ||--o{ INVENTORY_BALANCE : stores
    PRODUCT ||--o{ INVENTORY_MOVEMENT : ledger
    STORAGE_LOCATION ||--o{ INVENTORY_MOVEMENT : movement_location
    SUPPLIER ||--o{ PURCHASE_ORDER : receives_orders
    PURCHASE_ORDER ||--|{ PURCHASE_ORDER_LINE : contains
    PURCHASE_ORDER ||--o{ GOODS_RECEIPT : received_as
```

## Fulfillment rules

- Reservations reduce available stock without reducing on-hand stock.
- Pick lists are built from active reservations and retain the source sales-order line and storage location.
- Dispatch releases the corresponding reservation and posts a dispatch inventory movement in the same database transaction.
- Shipment and sales-order status changes are audited.
- Order cancellation releases active reservations and cancels undispatched shipments. Orders with dispatched quantities cannot be cancelled through the ordinary cancellation route.
- Inventory adjustments and dispatches must be represented in the inventory movement ledger; do not edit on-hand totals directly in application code.

## Build artifacts

GitHub Actions runs on pushes to `master` and `main`, and on pull requests targeting those branches. After restore/build/test passes, separate Windows x64 artifacts are published for the API and WPF desktop client. The API requires a configured PostgreSQL database and JWT settings at runtime.
