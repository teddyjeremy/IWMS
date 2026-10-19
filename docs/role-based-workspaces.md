# Role-based desktop workspaces

The desktop client personalizes its navigation and dashboard after the API authenticates the user. The API remains the authority for access control: hiding a navigation item is a usability feature, not a security boundary.

## Workspace visibility

| Role | Primary workspace |
|---|---|
| Administrator | All operational workspaces, reports, and user access |
| Warehouse Manager | Orders, fulfillment, transfers, counts, returns, inventory, purchasing, and reports |
| Purchaser | Inventory, purchase orders, approval KPIs, and reports |
| Warehouse Operator | Inventory, fulfillment, transfers, counts, and returns |
| Sales Officer | Customers, sales orders, fulfillment, inventory, and order KPIs |
| Viewer / Auditor | Read-oriented inventory, reporting, and audit KPIs |

The exact permissions for every API endpoint must continue to be enforced by ASP.NET Core authorization. If the backend role names or permissions change, update the desktop role map and its tests in the same change.

## UI delivery checklist

- [x] Improve global button, text input, and data-grid styling.
- [x] Identify the signed-in user's role in the sidebar.
- [x] Show role-appropriate navigation and dashboard KPIs.
- [x] Add an existing-customer editor and deactivation action for roles allowed by the API.
- [x] Add editable product detail views with category, unit, reorder threshold, cost, and optimistic concurrency version.
- [ ] Add safe sales-order detail editing only for eligible draft orders, with API-side state validation.
- [ ] Replace single-line transfer and return dialogs with multi-line document editors.
- [ ] Add clear validation summaries, empty states, loading states, and recoverable error messages.
- [ ] Add tests for role-to-navigation mapping and verify the API rejects unauthorized requests.
- [ ] Verify the packaged Windows client against the deployed HTTPS API after each release build.

## Role-map safety

The client currently uses the role names returned by the authenticated API. When a user has multiple roles, the combined visible workspace should be the union of the workspaces granted by those roles. Do not use UI visibility to imply that an API action is authorized.
