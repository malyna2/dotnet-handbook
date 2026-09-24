using System.Text.Json;
using QueryLab.Core;
using Xunit;

namespace QueryLab.Tests;

/// <summary>
/// One test per rung. Each checks correctness first (same rows as the plain-SQL reference), then
/// the acceptance criterion. Criteria are about work, not milliseconds — statements, rows, buffers,
/// plan nodes — so they hold on any machine. A starter rung fails with a message starting
/// "ACCEPTANCE:"; anything else failing means the code is broken, not merely slow.
/// </summary>
public sealed class RungTests(LabDatabase database)
{
    private static readonly IReadOnlyList<Rung> Rungs =
        Harness.DiscoverRungs(typeof(QueryLab.Rungs.OrderHistory).Assembly);

    private LabDb Lab => database.Lab;

    [Fact]
    public async Task Rung1_order_history_needs_at_most_two_statements()
    {
        var m = await MeasureAsync<OrderHistoryRung>();
        AssertSameResult(await Reference.OrderHistoryAsync(Lab), m.Result);
        Accept(m.PerCall.Statements <= 2,
            $"the page executed {m.PerCall.Statements:F0} statements per call; it needs at most 2.");
    }

    [Fact]
    public async Task Rung2_delivery_report_returns_no_duplicated_rows()
    {
        var m = await MeasureAsync<DeliveryReportRung>();
        var expected = await Reference.DeliveryReportAsync(Lab);
        AssertSameResult(expected, m.Result);
        var needed = expected.Count + expected.Sum(o => o.Lines.Count) + expected.Sum(o => o.Shipments.Count);
        Accept(m.PerCall.Rows <= needed,
            $"the server returned {m.PerCall.Rows:N0} rows for {needed:N0} orders + lines + shipments.");
    }

    [Fact]
    public async Task Rung3_sign_in_does_not_scan_customers()
    {
        var m = await MeasureAsync<SignInRung>();
        AssertSameResult(await Reference.CustomerAsync(Lab, SignInRung.SampleCustomerId), m.Result);
        var scan = SeqScanOn(m, "customers");
        Accept(scan is null,
            $"the plan reads the whole customers table ({scan?.NodeType}, {m.PerCall.SharedBlocks:N0} buffers) to find one row.");
    }

    [Fact]
    public async Task Rung4_webhook_uses_the_primary_key()
    {
        var m = await MeasureAsync<PartnerWebhookRung>();
        AssertSameResult(await Reference.OrderAsync(Lab, (long)PartnerWebhookRung.Payload.OrderNumber), m.Result);
        var scan = SeqScanOn(m, "orders");
        Accept(scan is null,
            $"a primary-key lookup became a {scan?.NodeType} over orders ({m.PerCall.SharedBlocks:N0} buffers).");
    }

    [Fact]
    public async Task Rung5_product_sales_are_answered_from_an_index_alone()
    {
        var m = await MeasureAsync<ProductSalesRung>();
        AssertSameResult(await Reference.ProductSalesAsync(Lab), m.Result);
        var indexOnly = m.Plans.SelectMany(p => p.Nodes)
            .FirstOrDefault(n => n.NodeType == "Index Only Scan" && n.RelationName == "order_lines");
        Accept(indexOnly is not null,
            $"order_lines is read with {string.Join(", ", NodeTypesOn(m, "order_lines"))} — every matching line visits the table ({m.PerCall.SharedBlocks:N0} buffers).");
    }

    [Fact]
    public async Task Rung6_customer_year_reads_at_most_50_buffers()
    {
        var m = await MeasureAsync<CustomerYearRung>();
        AssertSameResult(await Reference.CustomerYearAsync(Lab), m.Result);
        Accept(m.PerCall.SharedBlocks <= 50,
            $"{m.PerCall.SharedBlocks:N0} shared buffers to return {m.PerCall.Rows:N0} rows; it needs at most 50.");
    }

    [Fact]
    public async Task Rung7_deep_page_reads_at_most_100_buffers()
    {
        var m = await MeasureAsync<AdminOrdersPageRung>();
        AssertSameResult(await Reference.AdminPageAsync(Lab), m.Result);
        Accept(m.PerCall.SharedBlocks <= 100,
            $"{m.PerCall.SharedBlocks:N0} shared buffers to return one page of {AdminOrdersPageRung.PageSize}; it needs at most 100.");
    }

    [Fact]
    public async Task Rung8_customer_search_does_not_scan_customers()
    {
        var m = await MeasureAsync<CustomerSearchRung>();
        AssertSameResult(await Reference.CustomerSearchAsync(Lab), m.Result);
        var scan = SeqScanOn(m, "customers");
        Accept(scan is null,
            $"the search reads every customer ({scan?.NodeType}, {m.PerCall.SharedBlocks:N0} buffers).");
    }

    [Fact]
    public async Task Rung9_bestseller_after_warm_up_reads_at_most_100_buffers()
    {
        var m = await MeasureAsync<RecentSalesRung>();
        AssertSameResult(await Reference.RecentSalesAsync(Lab), m.Result);
        Accept(m.PerCall.SharedBlocks <= 100,
            $"after warm-up the bestseller's 20 rows cost {m.PerCall.SharedBlocks:N0} shared buffers; it needs at most 100.");
    }

    private async Task<RungMeasurement> MeasureAsync<T>() where T : Rung =>
        await Harness.MeasureAsync(Lab, Rungs.OfType<T>().Single(), runs: 2, explain: true, TestContext.Current.CancellationToken);

    private static PlanNode? SeqScanOn(RungMeasurement m, string table) =>
        m.Plans.SelectMany(p => p.Nodes).FirstOrDefault(n => n.NodeType.Contains("Seq Scan") && n.RelationName == table);

    private static IEnumerable<string> NodeTypesOn(RungMeasurement m, string table) =>
        m.Plans.SelectMany(p => p.Nodes).Where(n => n.RelationName == table).Select(n => n.NodeType).Distinct();

    private static void Accept(bool condition, string failure)
    {
        if (!condition) Assert.Fail("ACCEPTANCE: " + failure);
    }

    private static readonly JsonSerializerOptions Json = new() { Converters = { new NormalizedDecimal() } };

    // 8238914.00 and 8238914.0000 are the same amount: SQL numeric keeps its scale, so compare values, not text.
    private sealed class NormalizedDecimal : System.Text.Json.Serialization.JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AssertSameResult(object expected, object actual)
    {
        var e = JsonSerializer.Serialize(expected, expected.GetType(), Json);
        var a = JsonSerializer.Serialize(actual, expected.GetType(), Json);
        Assert.True(e == a, $"RESULT: the rung returned different rows from the reference.\nexpected: {Clip(e)}\nactual:   {Clip(a)}");
    }

    private static string Clip(string s) => s.Length <= 600 ? s : s[..600] + "…";
}
