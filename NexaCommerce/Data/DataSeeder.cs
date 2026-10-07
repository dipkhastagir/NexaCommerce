using Dapper;
using Microsoft.Data.SqlClient;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Data;

/// <summary>
/// Generates a realistic, reproducible demo dataset (fixed random seed) so that every
/// analytics module - forecasting, RFM, basket rules, ABC and risk scoring - has signal to work with.
/// </summary>
public class DataSeeder
{
    private readonly PasswordHasher _hasher;
    private readonly RiskScoringService _risk;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(PasswordHasher hasher, RiskScoringService risk, ILogger<DataSeeder> logger)
    {
        _hasher = hasher; _risk = risk; _logger = logger;
    }

    private record SeedProduct(string Sku, string Name, string Cat, string Brand, int Supplier, decimal Price,
        decimal Cost, decimal? Sale, int Stock, int Reorder, double Popularity, double Trend, bool Featured, string Desc);

    private static readonly SeedProduct[] Products =
    {
        new("EL-1001","NovaTech 43\" 4K Smart TV","electronics","NovaTech",1,48500,39800,45900,14,5,0.9,0.4,true,"A 43-inch 4K panel with HDR10, built-in streaming apps and three HDMI ports."),
        new("EL-1002","NovaTech Soundbar 2.1","electronics","NovaTech",1,12900,9400,null,22,6,1.0,0.2,false,"Compact 2.1 soundbar with wireless subwoofer and Bluetooth 5.0."),
        new("EL-1003","Wireless Earbuds Pro","electronics","Zenith",2,4200,2600,3690,60,15,3.2,0.5,true,"Active noise cancelling earbuds with 28 hours of total battery life."),
        new("EL-1004","Smart LED Bulb (Pack of 4)","electronics","Generic",1,1600,950,null,85,20,2.4,0.0,false,"Wi-Fi colour bulbs you can schedule from your phone."),
        new("EL-1005","Portable Bluetooth Speaker","electronics","NovaTech",1,3500,2300,null,38,10,1.8,-0.2,false,"Water-resistant speaker with 12-hour playback."),
        new("CP-2001","NovaTech Ultrabook 14","computers","NovaTech",1,89500,76000,84900,9,4,0.8,0.3,true,"14-inch ultrabook, 16 GB RAM, 512 GB SSD, 1.2 kg."),
        new("CP-2002","Wireless Optical Mouse","computers","NovaTech",1,850,420,null,140,30,4.0,0.1,false,"Silent-click mouse with 18-month battery life."),
        new("CP-2003","Laptop Backpack 15.6\"","computers","UrbanThread",4,2400,1350,null,55,12,2.2,0.1,false,"Padded laptop compartment, USB charging port, water-repellent fabric."),
        new("CP-2004","Mechanical Keyboard TKL","computers","NovaTech",1,5600,3700,null,26,8,1.3,0.3,false,"Tenkeyless keyboard with hot-swappable brown switches."),
        new("CP-2005","1TB Portable SSD","computers","NovaTech",1,9800,7600,8900,30,8,1.4,0.2,false,"USB-C portable SSD with up to 1,050 MB/s transfer speed."),
        new("CP-2006","27\" IPS Monitor","computers","NovaTech",1,24500,19800,null,7,4,0.7,0.0,false,"27-inch QHD IPS monitor, 75 Hz, adjustable stand."),
        new("CP-2007","USB-C Hub 7-in-1","computers","Generic",1,2900,1600,null,4,10,1.9,0.2,false,"HDMI, SD card, 3 x USB-A and 100 W passthrough charging."),
        new("MB-3001","Zenith Z12 Smartphone","mobile","Zenith",2,32999,27500,29999,18,6,1.6,0.3,true,"6.6-inch AMOLED, 128 GB storage, 50 MP camera."),
        new("MB-3002","Zenith Z12 Silicone Case","mobile","Zenith",2,690,240,null,120,25,3.0,0.3,false,"Soft-touch silicone case with raised camera lip."),
        new("MB-3003","65W GaN Fast Charger","mobile","Zenith",2,2200,1300,1890,75,15,2.6,0.4,false,"Charges a phone and a laptop together from one compact plug."),
        new("MB-3004","Tempered Glass Protector","mobile","Generic",2,350,90,null,200,40,3.4,0.2,false,"9H hardness glass with easy-align frame."),
        new("MB-3005","Smart Fitness Band","mobile","Zenith",2,3900,2500,null,0,8,1.1,-0.3,false,"Heart-rate, SpO2 and sleep tracking with 14-day battery."),
        new("MB-3006","10000mAh Power Bank","mobile","Zenith",2,1850,1100,null,64,15,2.3,0.0,false,"Slim power bank with 20 W PD output."),
        new("HK-4001","HomeCraft Electric Kettle 1.7L","home-kitchen","HomeCraft",3,2650,1700,null,40,10,2.0,0.0,false,"Stainless steel kettle with auto shut-off."),
        new("HK-4002","Non-stick Frying Pan 28cm","home-kitchen","HomeCraft",3,1950,1150,1690,48,12,2.1,0.1,false,"Granite-coated pan, induction compatible."),
        new("HK-4003","Drip Coffee Maker","home-kitchen","HomeCraft",3,5400,3900,null,16,5,0.9,0.5,true,"10-cup coffee maker with keep-warm plate."),
        new("HK-4004","Arabica Coffee Beans 500g","home-kitchen","Generic",3,1350,800,null,9,15,2.2,0.6,false,"Medium roast whole beans from Chittagong Hill Tracts growers."),
        new("HK-4005","Air Fryer 4.5L","home-kitchen","HomeCraft",3,8900,6600,7990,12,5,1.2,0.4,true,"Rapid-air fryer with 8 presets."),
        new("HK-4006","Glass Food Containers (Set of 5)","home-kitchen","HomeCraft",3,1450,820,null,70,15,1.6,0.0,false,"Oven-safe borosilicate containers with snap lids."),
        new("FS-5001","Cotton Panjabi - Indigo","fashion","UrbanThread",4,2800,1500,2390,45,10,1.7,0.3,false,"Hand-finished cotton panjabi with wooden buttons."),
        new("FS-5002","Everyday Sneakers","fashion","UrbanThread",4,4200,2700,null,36,10,1.5,0.0,false,"Breathable knit sneakers with cushioned sole."),
        new("FS-5003","Canvas Tote Bag","fashion","UrbanThread",4,750,320,null,95,20,2.0,0.1,false,"Heavy canvas tote with inner pocket."),
        new("FS-5004","Leather Wallet","fashion","UrbanThread",4,1650,900,null,52,12,1.3,-0.1,false,"Full-grain leather bifold with RFID lining."),
        new("FS-5005","Cotton Saree - Jamdani Print","fashion","UrbanThread",4,5200,3400,null,20,6,0.9,0.2,true,"Soft cotton saree with Jamdani-inspired print."),
        new("BK-6001","Dotted Notebook A5","books","PageTurner",5,420,180,null,180,40,3.1,0.1,false,"160 pages of 100 gsm dotted paper, lay-flat binding."),
        new("BK-6002","Gel Pen Set (12 colours)","books","PageTurner",5,380,160,null,160,40,2.7,0.1,false,"Smooth 0.5 mm gel pens in twelve colours."),
        new("BK-6003","Introduction to Algorithms","books","PageTurner",5,3200,2300,null,25,6,0.8,0.2,false,"The standard reference text for algorithms courses."),
        new("BK-6004","Desk Organizer","books","Generic",5,990,520,null,44,10,1.0,0.0,false,"Bamboo organizer with phone stand and pen slots."),
        new("SP-7001","Yoga Mat 6mm","sports","PeakFit",3,1850,1050,1590,58,12,1.9,0.4,false,"Non-slip TPE mat with carry strap."),
        new("SP-7002","Adjustable Dumbbells 20kg","sports","PeakFit",3,7800,5900,null,10,4,0.8,0.3,false,"Pair of spin-lock dumbbells, 2-10 kg each."),
        new("SP-7003","Resistance Band Set","sports","PeakFit",3,1200,560,null,6,12,1.7,0.6,false,"Five bands from 5 to 25 kg with door anchor."),
        new("SP-7004","Steel Water Bottle 1L","sports","PeakFit",3,950,480,null,110,20,2.5,0.1,false,"Double-wall insulated bottle, cold for 24 hours."),
        new("BT-8001","Lumina Vitamin C Serum","beauty","Lumina",3,1450,700,null,66,15,2.4,0.3,true,"15% vitamin C serum with hyaluronic acid."),
        new("BT-8002","Lumina Daily Sunscreen SPF50","beauty","Lumina",3,1100,540,990,80,18,2.8,0.4,false,"Lightweight sunscreen with no white cast."),
        new("BT-8003","Beard Trimmer","beauty","NovaTech",1,2700,1750,null,27,8,1.1,0.0,false,"Cordless trimmer with 20 length settings."),
    };

    /// <summary>Products frequently bought together. Drives the market-basket module.</summary>
    private static readonly (string A, string B, double P)[] Pairs =
    {
        ("CP-2001","CP-2002",0.65), ("CP-2001","CP-2003",0.55), ("CP-2001","CP-2007",0.40),
        ("MB-3001","MB-3002",0.70), ("MB-3001","MB-3004",0.60), ("MB-3001","MB-3003",0.45),
        ("HK-4003","HK-4004",0.75), ("EL-1001","EL-1002",0.45), ("SP-7001","SP-7003",0.50),
        ("SP-7002","SP-7004",0.40), ("BK-6001","BK-6002",0.60), ("BT-8001","BT-8002",0.55),
        ("CP-2004","CP-2002",0.45), ("FS-5001","FS-5003",0.25)
    };

    private static readonly string[] Cities = { "Dhaka", "Dhaka", "Dhaka", "Dhaka", "Chattogram", "Chattogram", "Sylhet", "Khulna", "Rajshahi", "Gazipur", "Narayanganj", "Cumilla" };
    private static readonly string[] FirstNames = { "Ayesha","Rahim","Karim","Nusrat","Tanvir","Farhana","Sakib","Mitu","Imran","Sadia","Arif","Jannat","Fahim","Rumana","Shihab","Tasnim","Rafi","Lamia","Hasan","Nadia","Rakib","Mehjabin","Sabbir","Priya","Anik","Shirin","Zubair","Tania","Mahin","Puja" };
    private static readonly string[] LastNames = { "Rahman","Hossain","Islam","Ahmed","Chowdhury","Khan","Akter","Das","Sarker","Uddin","Paul","Begum","Haque","Roy","Kabir" };

    public async Task<bool> SeedAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var userCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users");
        var productCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Products");
        if (userCount > 0 && productCount > 0) return false;

        var rnd = new Random(20260706);
        var now = DateTime.Now;
        var roles = (await conn.QueryAsync<(int RoleId, string Name)>("SELECT RoleId, Name FROM dbo.Roles")).ToDictionary(r => r.Name, r => r.RoleId);

        await using var tx = conn.BeginTransaction();

        // ---------- Staff accounts ----------
        int CreateUser(string name, string email, string phone, string role, string password, DateTime created)
        {
            var (hash, salt) = _hasher.Hash(password);
            return conn.ExecuteScalar<int>(@"INSERT INTO dbo.Users (FullName, Email, Phone, PasswordHash, PasswordSalt, RoleId, CreatedAt)
                OUTPUT INSERTED.UserId VALUES (@name, @email, @phone, @hash, @salt, @roleId, @created)",
                new { name, email, phone, hash, salt, roleId = roles[role], created }, tx);
        }

        var adminId = CreateUser("System Administrator", "admin@nexacommerce.com", "+880 1700-000001", "Admin", "Admin@123", now.AddDays(-200));
        CreateUser("Store Manager", "manager@nexacommerce.com", "+880 1700-000002", "Manager", "Manager@123", now.AddDays(-190));
        CreateUser("Warehouse Staff", "staff@nexacommerce.com", "+880 1700-000003", "Staff", "Staff@123", now.AddDays(-180));

        // ---------- Customers ----------
        var customers = new List<(int Id, string Name, string City, string Phone, double Weight)>();
        var demoId = CreateUser("Demo Customer", "customer@nexacommerce.com", "+880 1800-000000", "Customer", "Customer@123", now.AddDays(-160));
        customers.Add((demoId, "Demo Customer", "Dhaka", "+880 1800-000000", 2.0));
        for (int i = 0; i < 44; i++)
        {
            var name = FirstNames[rnd.Next(FirstNames.Length)] + " " + LastNames[rnd.Next(LastNames.Length)];
            var email = name.ToLower().Replace(" ", ".") + (i + 1) + "@example.com";
            var phone = "+880 17" + rnd.Next(10, 99) + "-" + rnd.Next(100000, 999999);
            var city = Cities[rnd.Next(Cities.Length)];
            var created = now.AddDays(-rnd.Next(125, 170));
            var id = CreateUser(name, email, phone, "Customer", "Customer@123", created);
            // Pareto-like activity: a few heavy buyers, a long tail of occasional ones
            var weight = Math.Pow(rnd.NextDouble(), 2.2) * 3 + 0.15;
            customers.Add((id, name, city, phone, weight));
        }
        foreach (var c in customers)
        {
            await conn.ExecuteAsync(@"INSERT INTO dbo.Addresses (UserId, Label, RecipientName, Phone, Line1, City, PostalCode, IsDefault)
                VALUES (@Id, 'Home', @Name, @Phone, @Line1, @City, @Postal, 1)",
                new { c.Id, c.Name, c.Phone, Line1 = $"House {rnd.Next(1, 120)}, Road {rnd.Next(1, 30)}", c.City, Postal = rnd.Next(1000, 9999).ToString() }, tx);
        }

        // ---------- Products ----------
        var catIds = (await conn.QueryAsync<(int Id, string Slug)>("SELECT CategoryId, Slug FROM dbo.Categories", transaction: tx)).ToDictionary(x => x.Slug, x => x.Id);
        var brandIds = (await conn.QueryAsync<(int Id, string Name)>("SELECT BrandId, Name FROM dbo.Brands", transaction: tx)).ToDictionary(x => x.Name, x => x.Id);
        var supplierIds = (await conn.QueryAsync<int>("SELECT SupplierId FROM dbo.Suppliers ORDER BY SupplierId", transaction: tx)).ToList();
        var productIds = new Dictionary<string, int>();
        foreach (var p in Products)
        {
            var id = await conn.ExecuteScalarAsync<int>(@"INSERT INTO dbo.Products (Sku, Name, Slug, Description, CategoryId, BrandId, SupplierId, Price, CostPrice, DiscountPrice,
                    StockQuantity, ReorderLevel, IsActive, IsFeatured, CreatedAt)
                OUTPUT INSERTED.ProductId
                VALUES (@Sku, @Name, @Slug, @Desc, @CategoryId, @BrandId, @SupplierId, @Price, @Cost, @Sale, @Stock, @Reorder, 1, @Featured, @Created)",
                new
                {
                    p.Sku, p.Name, Slug = Infrastructure.Slug.Make(p.Name), p.Desc, CategoryId = catIds[p.Cat],
                    BrandId = brandIds[p.Brand], SupplierId = supplierIds[Math.Min(p.Supplier, supplierIds.Count) - 1],
                    p.Price, p.Cost, p.Sale, p.Stock, p.Reorder, p.Featured, Created = now.AddDays(-150)
                }, tx);
            productIds[p.Sku] = id;
            await conn.ExecuteAsync(@"INSERT INTO dbo.StockMovements (ProductId, WarehouseId, MovementType, Quantity, BalanceAfter, Reference, Note, CreatedBy, CreatedAt)
                VALUES (@id, 1, 'OPENING', @Stock, @Stock, 'OPENING', 'Opening balance after stock count', @adminId, @at)",
                new { id, p.Stock, adminId, at = now.AddDays(-150) }, tx);
        }

        // ---------- Order history (120 days) ----------
        var weights = Products.Select(p => p.Popularity).ToArray();
        var pairLookup = Pairs.GroupBy(x => x.A).ToDictionary(g => g.Key, g => g.Select(x => (x.B, x.P)).ToList());
        double totalCustWeight = customers.Sum(c => c.Weight);
        int orderNo = 0;
        var reviewCandidates = new List<(int UserId, int ProductId, DateTime At)>();
        const int days = 120;

        for (int d = days; d >= 0; d--)
        {
            var day = now.Date.AddDays(-d);
            double progress = (days - d) / (double)days;
            double weekend = day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 1.35 : 1.0;
            double expected = (3.2 + 2.4 * progress) * weekend;
            int ordersToday = Poisson(rnd, expected);

            for (int k = 0; k < ordersToday; k++)
            {
                var cust = PickCustomer(rnd, customers, totalCustWeight);
                var created = day.AddHours(rnd.Next(9, 23)).AddMinutes(rnd.Next(0, 60));
                if (created > now) created = now.AddMinutes(-rnd.Next(5, 300));

                // pick 1-3 base items, weighted by popularity adjusted for trend over time
                var lines = new Dictionary<string, int>();
                int baseItems = rnd.NextDouble() < 0.55 ? 1 : rnd.NextDouble() < 0.7 ? 2 : 3;
                for (int b = 0; b < baseItems; b++)
                {
                    var idx = PickWeighted(rnd, Products, progress);
                    var sku = Products[idx].Sku;
                    lines[sku] = lines.GetValueOrDefault(sku) + (rnd.NextDouble() < 0.85 ? 1 : 2);
                    if (pairLookup.TryGetValue(sku, out var partners))
                        foreach (var (partner, prob) in partners)
                            if (rnd.NextDouble() < prob && !lines.ContainsKey(partner)) lines[partner] = 1;
                }

                // a handful of anomalous orders for the risk model to find
                bool anomaly = rnd.NextDouble() < 0.012;
                if (anomaly)
                {
                    var sku = Products[rnd.Next(Products.Length)].Sku;
                    lines[sku] = rnd.Next(6, 14);
                }

                decimal sub = 0;
                var lineRows = new List<(int ProductId, string Name, decimal Price, int Qty)>();
                foreach (var (sku, qty) in lines)
                {
                    var p = Products.First(x => x.Sku == sku);
                    var price = p.Sale ?? p.Price;
                    sub += price * qty;
                    lineRows.Add((productIds[sku], p.Name, price, qty));
                }

                string coupon = null; decimal discount = 0;
                if (rnd.NextDouble() < 0.08) { coupon = "WELCOME10"; discount = Math.Round(sub * 0.10m, 2); }
                else if (sub >= 5000 && rnd.NextDouble() < 0.10) { coupon = "FLAT500"; discount = 500; }
                decimal ship = sub >= 2000 ? 0 : 60;
                decimal total = sub - discount + ship;

                var payment = rnd.NextDouble() switch { < 0.55 => "COD", < 0.82 => "bKash", _ => "Card" };
                string status;
                if (d > 6) status = rnd.NextDouble() < 0.93 ? "Delivered" : "Cancelled";
                else if (d > 3) status = rnd.NextDouble() < 0.6 ? "Shipped" : "Delivered";
                else if (d > 1) status = rnd.NextDouble() < 0.5 ? "Processing" : "Confirmed";
                else status = rnd.NextDouble() < 0.6 ? "Pending" : "Confirmed";

                var paid = status == "Delivered" || (payment != "COD" && status != "Cancelled");
                var payStatus = status == "Cancelled" ? (payment != "COD" ? "Refunded" : "Unpaid") : paid ? "Paid" : "Unpaid";
                var shipCity = rnd.NextDouble() < 0.9 ? cust.City : Cities[rnd.Next(Cities.Length)];

                orderNo++;
                var orderId = await conn.ExecuteScalarAsync<int>(@"INSERT INTO dbo.Orders (OrderNumber, UserId, Status, PaymentMethod, PaymentStatus, SubTotal,
                        DiscountAmount, ShippingFee, TotalAmount, CouponCode, ShipName, ShipPhone, ShipAddress, ShipCity, CreatedAt, UpdatedAt)
                    OUTPUT INSERTED.OrderId
                    VALUES (@no, @uid, @status, @payment, @payStatus, @sub, @discount, @ship, @total, @coupon, @name, @phone, @addr, @city, @created, @created)",
                    new
                    {
                        no = "NX" + orderNo.ToString("D6"), uid = cust.Id, status, payment, payStatus, sub, discount, ship, total, coupon,
                        name = cust.Name, phone = cust.Phone, addr = $"House {rnd.Next(1, 120)}, Road {rnd.Next(1, 30)}", city = shipCity, created
                    }, tx);
                await conn.ExecuteAsync("UPDATE dbo.Orders SET OrderNumber = 'NX' + RIGHT('000000' + CAST(OrderId AS NVARCHAR(10)), 6) WHERE OrderId = @orderId", new { orderId }, tx);

                foreach (var l in lineRows)
                {
                    await conn.ExecuteAsync(@"INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, UnitPrice, Quantity, LineTotal)
                        VALUES (@orderId, @pid, @name, @price, @qty, @line)",
                        new { orderId, pid = l.ProductId, name = l.Name, price = l.Price, qty = l.Qty, line = l.Price * l.Qty }, tx);
                    if (status == "Delivered" && rnd.NextDouble() < 0.18) reviewCandidates.Add((cust.Id, l.ProductId, created.AddDays(rnd.Next(3, 9))));
                }

                await conn.ExecuteAsync(@"INSERT INTO dbo.OrderStatusHistory (OrderId, Status, Note, ChangedBy, ChangedAt) VALUES (@orderId, 'Pending', 'Order placed', @uid, @created)",
                    new { orderId, uid = cust.Id, created }, tx);
                if (status != "Pending")
                    await conn.ExecuteAsync(@"INSERT INTO dbo.OrderStatusHistory (OrderId, Status, Note, ChangedBy, ChangedAt) VALUES (@orderId, @status, @note, @adminId, @at)",
                        new { orderId, status, note = status == "Cancelled" ? "Cancelled at customer request" : "Status updated", adminId, at = created.AddDays(Math.Min(d, 3)).AddHours(2) }, tx);

                await conn.ExecuteAsync(@"INSERT INTO dbo.Payments (OrderId, Method, Amount, TransactionRef, Status, PaidAt, CreatedAt)
                    VALUES (@orderId, @payment, @total, @ref, @pstatus, @paidAt, @created)",
                    new
                    {
                        orderId, payment, total, @ref = payment == "COD" ? null : "SIM-" + rnd.Next(100000, 999999),
                        pstatus = payStatus == "Paid" ? "Success" : payStatus == "Refunded" ? "Refunded" : "Pending",
                        paidAt = payStatus == "Paid" ? created.AddDays(payment == "COD" ? Math.Min(d, 4) : 0) : (DateTime?)null, created
                    }, tx);
            }
        }

        // ---------- Reviews ----------
        var titles = new[] { "Exactly as described", "Great value", "Works well", "Good quality", "Fast delivery, solid product", "Decent for the price", "Would buy again", "Not bad" };
        var comments = new[]
        {
            "Arrived well packed and has worked without problems so far.",
            "Quality is better than I expected for this price.",
            "Does the job. Instructions could be clearer.",
            "Second time ordering this, consistent quality.",
            "Delivery took a day longer than promised but the product is good.",
            "Good, though the colour is slightly different from the photo."
        };
        foreach (var r in reviewCandidates.GroupBy(x => (x.UserId, x.ProductId)).Select(g => g.First()))
        {
            var rating = rnd.NextDouble() switch { < 0.05 => 2, < 0.15 => 3, < 0.50 => 4, _ => 5 };
            await conn.ExecuteAsync(@"INSERT INTO dbo.Reviews (ProductId, UserId, Rating, Title, Comment, IsApproved, CreatedAt)
                VALUES (@ProductId, @UserId, @rating, @title, @comment, @approved, @At)",
                new { r.ProductId, r.UserId, rating, title = titles[rnd.Next(titles.Length)], comment = comments[rnd.Next(comments.Length)], approved = rnd.NextDouble() < 0.9, r.At }, tx);
        }

        // ---------- Purchase orders ----------
        var poSamples = new[] { ("CP-2007", 40, "Received", -20), ("HK-4004", 60, "Ordered", -3), ("SP-7003", 50, "Draft", 0) };
        int poNo = 0;
        foreach (var (sku, qty, st, offset) in poSamples)
        {
            poNo++;
            var p = Products.First(x => x.Sku == sku);
            var poId = await conn.ExecuteScalarAsync<int>(@"INSERT INTO dbo.PurchaseOrders (PoNumber, SupplierId, WarehouseId, Status, OrderDate, ExpectedDate, ReceivedDate, TotalAmount, Notes, CreatedBy)
                OUTPUT INSERTED.PurchaseOrderId
                VALUES (@no, @sid, 1, @st, @od, @ed, @rd, @total, @notes, @adminId)",
                new
                {
                    no = "PO-" + now.Year + "-" + poNo.ToString("D4"), sid = supplierIds[Math.Min(p.Supplier, supplierIds.Count) - 1], st,
                    od = now.AddDays(offset - 7), ed = now.AddDays(offset + 3), rd = st == "Received" ? now.AddDays(offset) : (DateTime?)null,
                    total = qty * p.Cost, notes = "Replenishment for low stock", adminId
                }, tx);
            await conn.ExecuteAsync("INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost) VALUES (@poId, @pid, @qty, @cost)",
                new { poId, pid = productIds[sku], qty, cost = p.Cost }, tx);
        }

        // ---------- Notifications, contact messages and a return ----------
        await conn.ExecuteAsync(@"INSERT INTO dbo.Notifications (UserId, Title, Message, Link, Category)
            SELECT NULL, 'Low stock: ' + Name, Sku + ' is down to ' + CAST(StockQuantity AS NVARCHAR(10)) + ' units (reorder level ' + CAST(ReorderLevel AS NVARCHAR(10)) + ').',
                   '/Admin/Inventory/LowStock', 'Stock'
            FROM dbo.Products WHERE StockQuantity <= ReorderLevel", transaction: tx);
        await conn.ExecuteAsync(@"INSERT INTO dbo.Notifications (UserId, Title, Message, Link, Category) VALUES
            (NULL, 'Welcome to NexaCommerce', 'Demo data has been generated. Explore the Intelligence section for forecasts, segments and basket rules.', '/Admin/Intelligence', 'General')", transaction: tx);
        await conn.ExecuteAsync(@"INSERT INTO dbo.ContactMessages (Name, Email, Subject, Body, CreatedAt) VALUES
            ('Farhana Akter', 'farhana@example.com', 'Bulk order for office', 'We would like to order 25 wireless mice for our office. Is a corporate discount available?', @a),
            ('Imran Kabir', 'imran@example.com', 'Delivery to Sylhet', 'How many days does delivery to Sylhet usually take?', @b)",
            new { a = now.AddDays(-2), b = now.AddHours(-7) }, tx);

        var deliveredOrder = await conn.ExecuteScalarAsync<int?>(@"SELECT TOP 1 OrderId FROM dbo.Orders WHERE UserId = @demoId AND Status = 'Delivered' ORDER BY CreatedAt DESC", new { demoId }, tx);
        if (deliveredOrder.HasValue)
            await conn.ExecuteAsync(@"INSERT INTO dbo.Returns (OrderId, UserId, Reason, Status, CreatedAt) VALUES (@deliveredOrder, @demoId, 'One item arrived with a cracked casing. Requesting a replacement or refund.', 'Requested', @at)",
                new { deliveredOrder, demoId, at = now.AddDays(-1) }, tx);

        await conn.ExecuteAsync(@"INSERT INTO dbo.AuditLogs (UserId, UserEmail, Action, Entity, EntityId, Details, IpAddress)
            VALUES (@adminId, 'admin@nexacommerce.com', 'Seed', 'Database', NULL, @details, '127.0.0.1')",
            new { adminId, details = $"Generated {customers.Count} customers, {Products.Length} products and {orderNo} orders." }, tx);

        await tx.CommitAsync();
        _logger.LogInformation("Seeded {Orders} demo orders", orderNo);

        await _risk.RescoreAllAsync(connectionString);
        return true;
    }

    private static int Poisson(Random rnd, double lambda)
    {
        double l = Math.Exp(-lambda), p = 1; int k = 0;
        do { k++; p *= rnd.NextDouble(); } while (p > l);
        return k - 1;
    }

    private static (int Id, string Name, string City, string Phone, double Weight) PickCustomer(Random rnd,
        List<(int Id, string Name, string City, string Phone, double Weight)> list, double total)
    {
        var r = rnd.NextDouble() * total;
        foreach (var c in list) { r -= c.Weight; if (r <= 0) return c; }
        return list[^1];
    }

    private static int PickWeighted(Random rnd, SeedProduct[] products, double progress)
    {
        var w = products.Select(p => Math.Max(0.05, p.Popularity * (1 + p.Trend * (progress - 0.5) * 2))).ToArray();
        var r = rnd.NextDouble() * w.Sum();
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) return i; }
        return w.Length - 1;
    }
}
