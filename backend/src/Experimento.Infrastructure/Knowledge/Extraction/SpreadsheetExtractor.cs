using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>
/// Читает книги Excel (.xls и .xlsx — формат определяется автоматически) через
/// ExcelDataReader. Каждая строка данных листа превращается в "Заголовок: значение; …".
/// </summary>
public static class SpreadsheetExtractor
{
    static SpreadsheetExtractor()
    {
        // Требуется ExcelDataReader на .NET Core для старых кодовых страниц .xls.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static string Extract(Stream content)
    {
        using var reader = ExcelReaderFactory.CreateReader(content);
        var output = new StringBuilder();
        var firstSheet = true;

        do
        {
            string[]? header = null;
            var sheetLines = new List<string>();

            while (reader.Read())
            {
                var width = reader.FieldCount;
                var values = new string?[width];
                var allEmpty = true;
                for (var i = 0; i < width; i++)
                    values[i] = FormatCell(reader.GetValue(i), ref allEmpty);

                if (allEmpty) continue;
                var filled = values.Select(v => v ?? "").ToArray();

                if (header is null)
                {
                    header = filled;
                    continue;
                }
                sheetLines.Add(CsvTableExtractor.RenderRow(header, filled));
            }

            if (sheetLines.Count == 0 && header is not null)
                sheetLines.Add("Columns: " + string.Join(", ", header.Where(h => !string.IsNullOrWhiteSpace(h))));

            if (sheetLines.Count > 0)
            {
                if (!firstSheet) output.AppendLine();
                output.AppendLine($"# Sheet: {reader.Name}");
                output.Append(string.Join(Environment.NewLine, sheetLines));
                firstSheet = false;
            }
        } while (reader.NextResult());

        return output.ToString().Trim();
    }

    private static string? FormatCell(object? value, ref bool allEmpty)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTime dt:
                allEmpty = false;
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case double d:
                allEmpty = false;
                return d.ToString("0.######", CultureInfo.InvariantCulture);
            case bool b:
                allEmpty = false;
                return b ? "true" : "false";
            default:
                var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
                if (!string.IsNullOrEmpty(text)) allEmpty = false;
                return text;
        }
    }
}
