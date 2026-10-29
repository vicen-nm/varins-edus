using ClosedXML.Excel;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Infrastructure.Access;

// Builds the workbook of a report. Text goes in as text: nothing typed by a person runs as a formula.
public static class AccessReportExcel
{
    public static byte[] Build(AccessReport report)
    {
        using var workbook = new XLWorkbook();

        BuildSummary(workbook.AddWorksheet("Resumen"), report);
        BuildDetail(workbook.AddWorksheet("Detalle"), report);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private static string MethodLabel(string method) => method == AccessMethods.Qr ? "QR" : "Manual";

    private static void Header(IXLWorksheet sheet, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            sheet.Cell(row, i + 1).Value = titles[i];
        }

        sheet.Range(row, 1, row, titles.Length).Style.Font.Bold = true;
    }

    private static void BuildSummary(IXLWorksheet sheet, AccessReport report)
    {
        sheet.Cell(1, 1).Value = "Reporte del comedor";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = $"Del {report.From:yyyy-MM-dd} al {report.To:yyyy-MM-dd}";

        Header(sheet, 4, "Total", "QR", "Manual");
        sheet.Cell(5, 1).Value = report.Totals.Total;
        sheet.Cell(5, 2).Value = report.Totals.Qr;
        sheet.Cell(5, 3).Value = report.Totals.Manual;

        var row = 7;
        sheet.Cell(row, 1).Value = "Por día y servicio";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        Header(sheet, row, "Fecha", "Punto", "Servicio", "Total", "QR", "Manual");
        row++;

        foreach (var d in report.ByDay)
        {
            sheet.Cell(row, 1).Value = d.Date.ToString("yyyy-MM-dd");
            sheet.Cell(row, 2).Value = d.Point;
            sheet.Cell(row, 3).Value = d.Service;
            sheet.Cell(row, 4).Value = d.Total;
            sheet.Cell(row, 5).Value = d.Qr;
            sheet.Cell(row, 6).Value = d.Manual;
            row++;
        }

        row++;
        sheet.Cell(row, 1).Value = "Por grupo";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        Header(sheet, row, "Grupo", "Total");
        row++;

        foreach (var g in report.ByGroup)
        {
            sheet.Cell(row, 1).Value = g.Group ?? "Sin grupo";
            sheet.Cell(row, 2).Value = g.Total;
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildDetail(IXLWorksheet sheet, AccessReport report)
    {
        Header(sheet, 1, "Fecha", "Hora", "Cédula", "Nombre", "Apellidos", "Grupo", "Punto", "Servicio", "Método");

        var row = 2;

        foreach (var d in report.Details)
        {
            sheet.Cell(row, 1).Value = d.Date.ToString("yyyy-MM-dd");
            sheet.Cell(row, 2).Value = d.LocalTime.ToString("HH:mm:ss");
            sheet.Cell(row, 3).Value = d.IdentificationNumber;
            sheet.Cell(row, 4).Value = d.FirstName;
            sheet.Cell(row, 5).Value = d.LastName;
            sheet.Cell(row, 6).Value = d.Group ?? "Sin grupo";
            sheet.Cell(row, 7).Value = d.Point;
            sheet.Cell(row, 8).Value = d.Service;
            sheet.Cell(row, 9).Value = MethodLabel(d.Method);
            row++;
        }

        // Fixed widths: adjusting to contents is slow with tens of thousands of rows.
        double[] widths = [12, 10, 14, 18, 24, 12, 16, 16, 10];
        for (var i = 0; i < widths.Length; i++)
        {
            sheet.Column(i + 1).Width = widths[i];
        }

        sheet.SheetView.FreezeRows(1);
    }
}
