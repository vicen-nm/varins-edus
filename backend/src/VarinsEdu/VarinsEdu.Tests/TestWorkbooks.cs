using ClosedXML.Excel;

namespace VarinsEdu.Tests;

internal static class TestWorkbooks
{
    // Builds an .xlsx in memory. The first array is the header row.
    public static MemoryStream Create(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Estudiantes");

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = rows[r][c];
            }
        }

        return Save(workbook);
    }

    // A valid workbook with a sheet that has no content.
    public static MemoryStream Empty()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Vacia");

        return Save(workbook);
    }

    private static MemoryStream Save(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }
}
