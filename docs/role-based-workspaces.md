# Role-based desktop workspaces

The desktop client personalizes its navigation and dashboard after the API authenticates the user. The API remains the authority for access control: hiding a navigation item is a usability feature, not a security boundary.

## Workspace visibility

| Role | Primary workspace |
|---|---|
| Administrator | All operational workspaces, reports, and user access |
| Warehouse Manager | Orders, fulfillment, transfers, counts, returns, inventory, purchasing, and reports |
| Purchaser | Inventory, purchase orders, approval KPIs, and reports |
| Warehouse Operator | Inventory, fulfillment, transfers, counts, and returns |
| Sales Officer | Customers, sales order entry and draft editing, order confirmation/cancellation, inventory lookup, and order KPIs |
| Viewer / Auditor | Read-oriented inventory, reporting, and audit KPIs |

The exact permissions for every API endpoint must continue to be enforced by ASP.NET Core authorization. If the backend role names or permissions change, update the desktop role map and its tests in the same change.

## UI delivery checklist

- [x] Improve global button, text input, and data-grid styling.
- [x] Identify the signed-in user's role in the sidebar.
- [x] Show role-appropriate navigation and dashboard KPIs.
- [x] Add an existing-customer editor and deactivation action for roles allowed by the API.
- [x] Add editable product detail views with category, unit, reorder threshold, cost, and optimistic concurrency version.
- [x] Add API-side draft sales-order editing with state checks, optimistic concurrency, and fulfillment activity protection.
- [x] Add a multi-line sales-order editor to the desktop client and expose draft editing for draft orders; API checks and version-based concurrency remain in force.
- [x] Replace single-line transfer and return entry with multi-line document payloads.
- [ ] Finish clear validation summaries and empty states; loading feedback and recoverable workspace errors are implemented.
- [x] Add policy-level tests proving Sales Officer order permissions remain separate from warehouse fulfillment permissions.
- [ ] Add role-to-navigation mapping tests and endpoint-level integration tests for unauthorized requests.
- [ ] Verify the packaged Windows client against the deployed HTTPS API after each release build.

## Role-map safety

The client currently uses the role names returned by the authenticated API. When a user has multiple roles, the combined visible workspace should be the union of the workspaces granted by those roles. Do not use UI visibility to imply that an API action is authorized.
