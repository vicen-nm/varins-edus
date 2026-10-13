using VarinsEdu.Infrastructure.Imports;
using Xunit;

namespace VarinsEdu.Tests;

public class ExcelReaderTests
{
    [Fact]
    public void Reads_headers_and_rows_and_skips_blank_rows()
    {
        using var file = TestWorkbooks.Create(
            new[] { "Cédula", "Nombre" },
            new[] { "1-1111-1111", "Ana" },
            new[] { "", "" },
            new[] { "2-2222-2222", "Beto" });

        var sheet = ExcelReader.Read(file);

        Assert.Equal(new[] { "Cédula", "Nombre" }, sheet.Headers);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(2, sheet.Rows[0].Number);

        // The blank row 3 is skipped, but the numbering still matches Excel.
        Assert.Equal(4, sheet.Rows[1].Number);
        Assert.Equal("Beto", sheet.Rows[1].Cells[1]);
    }

    [Fact]
    public void Keeps_leading_zeros_in_text_cells()
    {
        using var file = TestWorkbooks.Create(new[] { "Cédula" }, new[] { "00123" });

        var sheet = ExcelReader.Read(file);

        Assert.Equal("00123", sheet.Rows[0].Cells[0]);
    }

    [Fact]
    public void Rejects_files_that_are_not_excel()
    {
        using var file = new MemoryStream("this is not an excel file"u8.ToArray());

        var error = Assert.Throws<ImportFileException>(() => ExcelReader.Read(file));

        Assert.Equal("invalid_file", error.Code);
    }

    [Fact]
    public void Rejects_a_workbook_with_no_content()
    {
        using var file = TestWorkbooks.Empty();

        var error = Assert.Throws<ImportFileException>(() => ExcelReader.Read(file));

        Assert.Equal("empty_file", error.Code);
    }

    [Fact]
    public void Rejects_files_with_too_many_rows()
    {
        var rows = new string[ExcelReader.MaxRows + 2][];
        rows[0] = new[] { "Cédula" };

        for (var i = 1; i < rows.Length; i++)
        {
            rows[i] = new[] { i.ToString() };
        }

        using var file = TestWorkbooks.Create(rows);

        var error = Assert.Throws<ImportFileException>(() => ExcelReader.Read(file));

        Assert.Equal("too_many_rows", error.Code);
    }
}
