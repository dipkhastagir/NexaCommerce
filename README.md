# NexaCommerce — Inventory-Aware E-Commerce Platform with a Decision-Support Layer

ASP.NET Core 8 MVC · Dapper · Microsoft SQL Server · Stored procedures & views · Cookie auth (PBKDF2) · REST API · Chart.js

NexaCommerce extends the "e-commerce management foundation" from the 3BL internship report into a complete
system: a customer storefront (cart, checkout, payments, tracking, reviews, returns), a role-based back office
(catalog, inventory ledger, purchasing, orders, customers, reports, audit log) and an **Intelligence** module that
turns transaction data into decisions — the part designed to grow into a research project.

---

## 1. Run it (Visual Studio 2022 + SQL Server)

**You need a SQL Server *engine* running.** SSMS is only the client. If you can connect to a server in SSMS, you're fine.
If not, install *SQL Server 2022 Express* or *Developer* (free) first.

1. Unzip and open **`NexaCommerce.sln`** in Visual Studio 2022 (with the *ASP.NET and web development* workload, .NET 8 SDK).
2. Open **`NexaCommerce/appsettings.json`** and check the server name:
   ```json
   "DefaultConnection": "Server=localhost;Database=NexaCommerceDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
   ```
   Use the **Server name** shown in SSMS's connect dialog. Examples:
   `localhost` · `.\\SQLEXPRESS` · `DESKTOP-ABC123\\SQLEXPRESS` · `(localdb)\\MSSQLLocalDB` (backslash written as `\\` in JSON).
   If you don't change it, the app also tries `localhost`, `.\SQLEXPRESS` and LocalDB automatically.
3. Press **F5** (profile *NexaCommerce*). NuGet restores Dapper and Microsoft.Data.SqlClient automatically.
   Accept the HTTPS development certificate prompt the first time.
4. First start takes ~10–20 seconds. The app will automatically:
   - create the database `NexaCommerceDB`,
   - run `Database/01_Schema.sql` → `02_Views.sql` → `03_StoredProcedures.sql` → `04_SeedData.sql`,
   - generate demo users, 40 products and ~4 months of realistic order history,
   - score every order for risk.
5. The browser opens at `https://localhost:7177`. If SQL Server can't be reached you get a **Setup** page that
   explains the fix and has a *Try again* button (no restart needed).

Refresh `NexaCommerceDB` in SSMS to see all tables, views and stored procedures.

### Demo accounts
| Role | E-mail | Password |
|---|---|---|
| Admin | admin@nexacommerce.com | Admin@123 |
| Manager | manager@nexacommerce.com | Manager@123 |
| Staff | staff@nexacommerce.com | Staff@123 |
| Customer | customer@nexacommerce.com | Customer@123 |

Back office: `https://localhost:7177/Admin` (sign in as Admin/Manager/Staff).
Coupons: `WELCOME10`, `FLAT500` (orders ≥ ৳5,000), `RESEARCH15`.
Simulated payment: bKash/Card numbers ending in `0000` are declined; anything else is approved.

### Reset the demo data
Delete the database in SSMS (`DROP DATABASE NexaCommerceDB`) and press F5 again — everything is rebuilt.
The random seed is fixed, so the regenerated data is identical (useful for reproducible experiments).

### Troubleshooting
| Symptom | Fix |
|---|---|
| Setup page: "Could not reach SQL Server" | Wrong server name, or the *SQL Server (MSSQLSERVER/SQLEXPRESS)* Windows service is stopped. |
| "Login failed for user" | Your Windows account lacks rights on that instance; use SQL auth: `Server=...;Database=NexaCommerceDB;User Id=sa;Password=...;TrustServerCertificate=True` |
| Page looks unstyled | Bootstrap, icons, fonts and Chart.js load from CDNs — needs an internet connection. |
| NuGet restore fails | Tools → Options → NuGet Package Manager → Package Sources: ensure nuget.org is enabled. |

---

## 2. What's inside

**87 full pages** (100 Razor files incl. partials/layouts), 49 C# files, 4 SQL scripts, ~10,700 lines.

### Storefront (customers)
Home with live "selling fastest" board and real basket pairs · Shop with category/brand/price/stock filters, sorting,
paging · Deals · Product page with ratings histogram, verified reviews and **frequently-bought-together** from
association rules · Compare (up to 4) · Wishlist · Cart with free-delivery meter and basket-based suggestions ·
Checkout with saved addresses, live coupon validation and simulated bKash/Card gateway · Confirmation ·
My orders · Order details · Visual order tracking · Cancel · Returns (30-day window) · Reviews ·
Register · Login · Forgot/Reset password (one-time, 30-minute tokens, simulated e-mail outbox) · Profile ·
Change password · Address book · FAQ · About · Contact · Privacy · Terms · 404/403 · Setup.

### Back office (Admin / Manager / Staff)
Dashboard (30-day revenue vs previous period, orders/revenue chart, category mix, top products, low stock) ·
Products (list, details with forecast chart and ledger, create/edit with margin check, archive-or-delete) ·
Categories (hierarchy, icon picker) · Brands · Suppliers (details with POs) · Warehouses · Inventory
(level meters, adjustments with reasons, low-stock list, **full stock ledger** with running balance) ·
Purchase orders (draft → ordered → received; receiving updates stock & cost atomically; auto-drafted from
recommendations) · Orders (filters, CSV export, status workflow, history timeline, printable invoice, risk banner) ·
Customers (LTV, RFM segment) · Reviews moderation · Returns & refunds · Coupons with usage stats ·
Customer messages · Notifications · Reports (sales by date range/payment/city, inventory valuation, product
performance) · Users & roles + permission matrix · Audit log · Settings · REST API reference.

### Intelligence (research layer)
| Module | Method |
|---|---|
| Demand forecast | Holt's linear exponential smoothing per product; α, β grid-searched; 14-day hold-out MAE / RMSE / sMAPE; skill vs naive baseline; 95% intervals; days-to-stock-out |
| Reorder planner | Safety stock `z·σ·√L`, reorder point `μ·L + SS`, EOQ `√(2DS/H)`, open POs counted, one-click PO per supplier |
| Customer segments | RFM quintile scoring → 10 named segments with recommended actions |
| Basket rules | Pairwise association rules: support, confidence, lift (drives storefront recommendations) |
| ABC analysis | Pareto classification by revenue, 30/90/180-day windows |
| Order risk | Explainable additive rule model (account age, z-scores, quantity, velocity, address mismatch, COD value, coupon) with stored reasons |
| Research datasets | Pseudonymised CSV exports: orders, order lines, daily demand panel, RFM features, products, stock ledger |
| Methodology | In-app documentation of every model and suggested research extensions |

### REST API (same service layer as the web UI)
`/api/products`, `/api/categories`, `/api/orders`, `/api/intelligence/*` — see *Back office → REST API*.

---

## 3. Architecture

```
Browser ──► Razor MVC controllers ─┐
Mobile / notebook ─► API controllers ┴─► Services (business rules) ─► Dapper ─► SQL Server
                                             │                         (tables · views · stored procedures)
                                             └─► Intelligence services (forecast, EOQ, RFM, rules, ABC, risk)
```

| Folder | Contents |
|---|---|
| `Database/` | `01_Schema.sql` (23 tables) · `02_Views.sql` (8 views) · `03_StoredProcedures.sql` (14 procedures) · `04_SeedData.sql` |
| `Data/` | Connection factory, startup initializer (server discovery, DB creation, script runner), demo-data generator |
| `Services/` | Auth (PBKDF2), catalog, inventory, purchasing, cart, orders, payments (`IPaymentGateway`), reports … |
| `Services/Intelligence/` | Forecasting, Reorder, Segmentation, BasketAnalysis, AbcAnalysis, RiskScoring, DatasetExport |
| `Controllers/` | Storefront + `Api/` |
| `Areas/Admin/` | Back-office controllers and views |

Key design points: stock changes only happen through stored procedures that also write the ledger
(`sp_PlaceOrder`, `sp_UpdateOrderStatus`, `sp_AdjustStock`, `sp_ReceivePurchaseOrder`), so on-hand quantity
always equals the sum of movements; checkout is a single transaction with row locks so concurrent buyers
can't oversell or over-use a coupon; products with sales history are archived instead of deleted;
every form is CSRF-protected; authorisation uses three policies (BackOffice, Management, AdminOnly).

---

## 4. Turning it into research

Ideas the exported datasets and code support directly:
1. **Forecasting benchmark** — compare Holt vs Holt-Winters, Croston/TSB, LightGBM and conformal prediction intervals on the daily-demand panel; measure the effect on stock-outs and holding cost by simulation.
2. **Explainable fraud scoring** — the rule model is a transparent baseline; train gradient boosting / isolation forest on the orders dataset and compare precision at fixed review capacity, with SHAP explanations.
3. **Recommendation impact** — A/B test the basket-rule widgets (product vs cart placement) on add-to-cart rate.
4. **Customer value** — BG/NBD + Gamma-Gamma CLV vs RFM segments for retention targeting.

Because the synthetic history is generated from a known process (fixed seed, defined product affinities,
trends, weekly seasonality and injected anomalies), every model can be validated against ground truth before
being applied to real store data.
#   N e x a C o m m e r c e  
 