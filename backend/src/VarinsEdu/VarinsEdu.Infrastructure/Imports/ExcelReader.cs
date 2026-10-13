using System.IO.Compression;
using ClosedXML.Excel;

namespace VarinsEdu.Infrastructure.Imports;

public static class ExcelReader
{
    public const int MaxRows = 5000;
    public const int MaxColumns = 50;

    // An .xlsx file is a zip. This limit protects against zip bombs:
    // tiny files that unpack to gigabytes.
    private const long MaxUncompressedBytes = 50 * 1024 * 1024;

    // The stream must be seekable. Any unreadable file is reported with the same stable code.
    public static ExcelSheet Read(Stream stream)
    {
        try
        {
            EnsureReasonableSize(stream);
            return ReadSheet(stream);
        }
        catch (ImportFileException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ImportFileException("invalid_file");
        }
    }

    private static void EnsureReasonableSize(Stream stream)
    {
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (zip.Entries.Sum(entry => entry.Length) > MaxUncompressedBytes)
            {
                throw new ImportFileException("invalid_file");
            }
        }

        stream.Position = 0;
    }

    private static ExcelSheet ReadSheet(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new ImportFileException("invalid_file");
        var used = sheet.RangeUsed() ?? throw new ImportFileException("empty_file");

        var columnCount = used.ColumnCount();
        if (columnCount > MaxColumns)
        {
            throw new ImportFileException("too_many_columns");
        }

        var firstRow = used.FirstRow().RowNumber();
        var lastRow = used.LastRow().RowNumber();
        var firstColumn = used.FirstColumn().ColumnNumber();

        if (lastRow - firstRow > MaxRows)
        {
            throw new ImportFileException("too_many_rows");
        }

        // Cell text as Excel shows it, so a text cell like "00123" keeps its leading zeros.
        List<string> ReadRow(int rowNumber) => Enumerable.Range(0, columnCount)
            .Select(i => sheet.Cell(rowNumber, firstColumn + i).GetFormattedString().Trim())
            .ToList();

        var headers = ReadRow(firstRow);

        if (headers.All(string.IsNullOrEmpty))
        {
            throw new ImportFileException("empty_file");
        }

        var rows = new List<ExcelRow>();

        for (var rowNumber = firstRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var cells = ReadRow(rowNumber);

            // Blank rows are skipped, but the other rows keep the number they have in Excel.
            if (cells.All(string.IsNullOrEmpty))
            {
                continue;
            }

            rows.Add(new ExcelRow(rowNumber, cells));
        }

        return new ExcelSheet(headers, rows);
    }
}
