using Npgsql;
using QueryLab.Core;

namespace QueryLab.Tests;

/// <summary>
/// What each rung must return, computed with plain SQL — independently of EF Core and of your fix.
/// A faster query that returns different rows is not a fix.
/// </summary>
internal static class Reference
{
    public static async Task<IReadOnlyList<OrderHistoryRow>> OrderHistoryAsync(LabDb lab)
    {
        var orders = await ListAsync(lab, $"""
            SELECT id, created_at, status, total FROM orders
            WHERE customer_id = {OrderHistoryRung.CustomerId}
            ORDER BY created_at DESC, id DESC LIMIT {OrderHistoryRung.PageSize}
            """, r => (Id: r.GetInt64(0), Created: r.GetFieldValue<DateTimeOffset>(1), Status: r.GetString(2), Total: r.GetDecimal(3)));
        var lines = await LinesAsync(lab, orders.Select(o => o.Id));
        return orders.Select(o => new OrderHistoryRow(o.Id, o.Created, o.Status, o.Total, lines[o.Id])).ToList();
    }

    public static async Task<IReadOnlyList<OrderExportRow>> DeliveryReportAsync(LabDb lab)
    {
        var orders = await ListAsync(lab, """
            SELECT id, created_at, total FROM orders
            WHERE customer_id = $1 AND status = 'shipped' AND created_at >= $2 AND created_at < $3
            ORDER BY id
            """, r => (Id: r.GetInt64(0), Created: r.GetFieldValue<DateTimeOffset>(1), Total: r.GetDecimal(2)),
            DeliveryReportRung.CustomerId, DeliveryReportRung.From, LabDb.Now);
        var ids = orders.Select(o => o.Id).ToArray();
        var lines = await LinesAsync(lab, ids);
        var shipments = (await ListAsync(lab,
                "SELECT order_id, carrier, shipped_at, tracking_code FROM shipments WHERE order_id = ANY($1) ORDER BY id",
                r => (OrderId: r.GetInt64(0), Row: new ShipmentRow(r.GetString(1), r.GetFieldValue<DateTimeOffset>(2), r.GetString(3))),
                ids))
            .ToLookup(s => s.OrderId, s => s.Row);
        return orders.Select(o => new OrderExportRow(o.Id, o.Created, o.Total, lines[o.Id], shipments[o.Id].ToList())).ToList();
    }

    public static async Task<CustomerRow> CustomerAsync(LabDb lab, long id) =>
        (await ListAsync(lab, "SELECT id, email, name FROM customers WHERE id = $1",
            r => new CustomerRow(r.GetInt64(0), r.GetString(1), r.GetString(2)), id)).Single();

    public static async Task<OrderRow> OrderAsync(LabDb lab, long id) =>
        (await OrdersAsync(lab, "SELECT id, customer_id, status, created_at, total FROM orders WHERE id = $1", id)).Single();

    public static async Task<ProductSales> ProductSalesAsync(LabDb lab)
    {
        var row = (await ListAsync(lab,
            "SELECT count(*)::int, coalesce(sum(quantity), 0)::bigint, coalesce(sum(quantity * unit_price), 0) FROM order_lines WHERE product_id = $1",
            r => (Lines: r.GetInt32(0), Units: r.GetInt64(1), Revenue: r.GetDecimal(2)), ProductSalesRung.ProductId)).Single();
        return new ProductSales(ProductSalesRung.ProductId, row.Lines, row.Units, row.Revenue);
    }

    public static Task<IReadOnlyList<OrderRow>> CustomerYearAsync(LabDb lab) =>
        OrdersAsync(lab, """
            SELECT id, customer_id, status, created_at, total FROM orders
            WHERE customer_id = $1 AND created_at >= $2 ORDER BY created_at DESC, id DESC
            """, CustomerYearRung.CustomerId, CustomerYearRung.Since);

    public static Task<IReadOnlyList<OrderRow>> AdminPageAsync(LabDb lab) =>
        OrdersAsync(lab, $"""
            SELECT id, customer_id, status, created_at, total FROM orders
            ORDER BY created_at DESC, id DESC
            OFFSET {(AdminOrdersPageRung.PageNumber - 1) * AdminOrdersPageRung.PageSize} LIMIT {AdminOrdersPageRung.PageSize}
            """);

    public static async Task<IReadOnlyList<CustomerRow>> CustomerSearchAsync(LabDb lab) =>
        await ListAsync(lab,
            "SELECT id, email, name FROM customers WHERE strpos(lower(email), lower($1)) > 0 ORDER BY id",
            r => new CustomerRow(r.GetInt64(0), r.GetString(1), r.GetString(2)), CustomerSearchRung.Term);

    public static async Task<IReadOnlyList<RecentSaleRow>> RecentSalesAsync(LabDb lab) =>
        await ListAsync(lab,
            $"SELECT id, order_id, quantity, unit_price FROM order_lines WHERE product_id = $1 ORDER BY id DESC LIMIT {RecentSalesRung.Take}",
            r => new RecentSaleRow(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetDecimal(3)), RecentSalesRung.Bestseller);

    private static async Task<Dictionary<long, IReadOnlyList<OrderLineRow>>> LinesAsync(LabDb lab, IEnumerable<long> orderIds)
    {
        var ids = orderIds.ToArray();
        var lines = (await ListAsync(lab,
                "SELECT order_id, product_id, quantity, unit_price FROM order_lines WHERE order_id = ANY($1) ORDER BY id",
                r => (OrderId: r.GetInt64(0), Row: new OrderLineRow(r.GetInt64(1), r.GetInt32(2), r.GetDecimal(3))), ids))
            .ToLookup(l => l.OrderId, l => l.Row);
        return ids.ToDictionary(id => id, id => (IReadOnlyList<OrderLineRow>)lines[id].ToList());
    }

    private static async Task<IReadOnlyList<OrderRow>> OrdersAsync(LabDb lab, string sql, params object[] args) =>
        await ListAsync(lab, sql,
            r => new OrderRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetFieldValue<DateTimeOffset>(3), r.GetDecimal(4)), args);

    private static async Task<List<T>> ListAsync<T>(LabDb lab, string sql, Func<NpgsqlDataReader, T> map, params object[] args)
    {
        await using var cmd = lab.DataSource.CreateCommand(sql);
        foreach (var arg in args) cmd.Parameters.Add(new NpgsqlParameter { Value = arg });
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await reader.ReadAsync()) list.Add(map(reader));
        return list;
    }
}
