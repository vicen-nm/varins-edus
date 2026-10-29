using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Access;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/access/report")]
[Authorize(Policy = PermissionKeys.AccessView)]
public class AccessReportController(AccessReportService reports) : ControllerBase
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // The summary only: the per-student detail (with cédulas) is only in the Excel file.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var outcome = await reports.BuildAsync(from, to, ct);

        if (outcome.Error is { } code)
        {
            return this.AccessProblem(code);
        }

        var report = outcome.Value!;

        return Ok(new { report.From, report.To, report.Totals, report.ByDay, report.ByGroup });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var outcome = await reports.ExportAsync(from, to, ct);

        if (outcome.Error is { } code)
        {
            return this.AccessProblem(code);
        }

        // The file has personal data of minors: nobody may keep a copy.
        Response.Headers.CacheControl = "no-store";

        var file = outcome.Value!;

        return File(file.Bytes, ExcelType, file.FileName);
    }
}
