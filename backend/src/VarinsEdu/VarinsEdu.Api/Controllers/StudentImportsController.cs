using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VarinsEdu.Api.Errors;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Imports;

namespace VarinsEdu.Api.Controllers;

[ApiController]
[Route("api/imports/students")]
[Authorize(Policy = PermissionKeys.StudentsImport)]
public class StudentImportsController(StudentImportService importService) : ControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    // Reads the Excel file and shows what an import would do, without saving anything.
    // "mapping" is optional JSON: { "identificationNumber": "Cédula", "firstName": "Nombre", ... }
    [HttpPost("preview")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Preview(
        [FromForm] IFormFile? file,
        [FromForm] string? mapping,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, "file_required", "An Excel file is required.");
        }

        if (file.Length > MaxFileBytes)
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, "file_too_large", "The file cannot be larger than 5 MB.");
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, "invalid_file", "Only .xlsx files are accepted.");
        }

        Dictionary<string, string>? parsedMapping = null;

        if (!string.IsNullOrWhiteSpace(mapping))
        {
            try
            {
                parsedMapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mapping);
            }
            catch (JsonException)
            {
                return this.ApiProblem(StatusCodes.Status400BadRequest, "invalid_mapping", "The column mapping is not valid.");
            }
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        try
        {
            return Ok(await importService.PreviewAsync(buffer, parsedMapping, ct));
        }
        catch (ImportFileException ex)
        {
            return this.ApiProblem(StatusCodes.Status400BadRequest, ex.Code, "The file could not be read.");
        }
    }
}
