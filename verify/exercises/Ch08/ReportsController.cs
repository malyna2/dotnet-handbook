// Chapter 8, Exercises → Find the bug: a controller that blocks on async work.
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ch08.Exercises;

public sealed record Subscriber(int Id, int ReportId, string Email);
public sealed record Report(int Id, string Body);

public interface IReportService
{
    Task<Report> BuildReportAsync(int id, CancellationToken ct = default);
}

public interface IMailer
{
    Task SendAsync(string to, Report report, CancellationToken ct = default);
}

public sealed class ReportsDb(DbContextOptions<ReportsDb> options) : DbContext(options)
{
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
}

/// <summary>The sample as printed in the chapter.</summary>
public sealed class BuggyReportsController(IReportService reportService, ReportsDb db, IMailer mailer) : ControllerBase
{
    private readonly IReportService _reportService = reportService;
    private readonly ReportsDb _db = db;
    private readonly IMailer _mailer = mailer;

    [HttpGet("/reports/{id:int}")]
    public IActionResult GetReport(int id)
    {
        var report = _reportService.BuildReportAsync(id).Result;

        var recipients = _db.Subscribers
            .Where(s => s.ReportId == id)
            .ToList();

        Parallel.ForEach(recipients, r =>
        {
            _mailer.SendAsync(r.Email, report).Wait();
        });

        return Ok(report);
    }
}

/// <summary>
/// The fix from the answer: async all the way down, bounded concurrency for the I/O fan-out,
/// and a CancellationToken threaded from the action signature to every call.
/// </summary>
public sealed class FixedReportsController(IReportService reportService, ReportsDb db, IMailer mailer) : ControllerBase
{
    [HttpGet("/reports/{id:int}")]
    public async Task<IActionResult> GetReport(int id, CancellationToken ct)
    {
        var report = await reportService.BuildReportAsync(id, ct);

        var recipients = await db.Subscribers
            .Where(s => s.ReportId == id)
            .ToListAsync(ct);

        await Parallel.ForEachAsync(
            recipients,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (r, token) => await mailer.SendAsync(r.Email, report, token));

        return Ok(report);
    }
}
