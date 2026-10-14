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
public class StudentImportsController(StudentImportService previewService, StudentImportCommitter committer)
    : ControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    // Shows what an import would do, without saving anything.
    // "mapping" is optional JSON: { "identificationNumber": "Cédula", "firstName": "Nombre", ... }
    [HttpPost("preview")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Preview(
        [FromForm] IFormFile? file,
        [FromForm] string? mapping,
        CancellationToken ct)
    {
        var (error, buffer, parsedMapping) = await ReadInputAsync(file, mapping, ct);

        if (error is not null)
        {
            return error;
        }

        await using (buffer!)
        {
            try
            {
                return Ok(await previewService.PreviewAsync(buffer!, parsedMapping, ct));
            }
            catch (ImportFileException ex)
            {
                return this.ApiProblem(StatusCodes.Status400BadRequest, ex.Code, "The file could not be read.");
            }
        }
    }

    // Saves the import. Returns each new student's activation code ONCE: it is stored only as a fingerprint.
    [HttpPost("commit")]
    [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Commit(
        [FromForm] IFormFile? file,
        [FromForm] string? mapping,
        CancellationToken ct)
    {
        var (error, buffer, parsedMapping) = await ReadInputAsync(file, mapping, ct);

        if (error is not null)
        {
            return error;
        }

        await using (buffer!)
        {
            try
            {
                var result = await committer.CommitAsync(buffer!, file!.FileName, parsedMapping, ct);

                // The response carries secrets: no browser or proxy may keep a copy.
                Response.Headers.CacheControl = "no-store";

                return Ok(result);
            }
            catch (ImportFileException ex)
            {
                return this.ApiProblem(StatusCodes.Status400BadRequest, ex.Code, "The file could not be imported.");
            }
        }
    }

    // Shared checks for both endpoints: file present, small, .xlsx, and a valid optional mapping.
    private async Task<(IActionResult? Error, MemoryStream? Buffer, Dictionary<string, string>? Mapping)> ReadInputAsync(
        IFormFile? file,
        string? mapping,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return (this.ApiProblem(StatusCodes.Status400BadRequest, "file_required", "An Excel file is required."), null, null);
        }

        if (file.Length > MaxFileBytes)
        {
            return (this.ApiProblem(StatusCodes.Status400BadRequest, "file_too_large", "The file cannot be larger than 5 MB."), null, null);
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return (this.ApiProblem(StatusCodes.Status400BadRequest, "invalid_file", "Only .xlsx files are accepted."), null, null);
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
                return (this.ApiProblem(StatusCodes.Status400BadRequest, "invalid_mapping", "The column mapping is not valid."), null, null);
            }
        }

        var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        return (null, buffer, parsedMapping);
    }
}
