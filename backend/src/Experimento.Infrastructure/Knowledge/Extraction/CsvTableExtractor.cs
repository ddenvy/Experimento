using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>
/// Разбирает CSV в семантически осмысленные строки.
/// Первая непустая строка считается заголовком; каждая последующая строка
/// превращается в "Заголовок: значение; Заголовок: значение".
/// Учитывает кавычки RFC 4180 и автоматически определяет разделитель (, ; TAB |).
/// </summary>
public static class CsvTableExtractor
{
    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];

    public static string Extract(byte[] bytes)
    {
        var encoding = TextEncodingDetector.Detect(bytes);
        var raw = encoding.GetString(bytes);
        // BOM (U+FEFF) первой строки ломает имя первого столбца.
        raw = raw.TrimStart((char)0xFEFF);
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var delimiter = DetectDelimiter(raw);

        using var reader = new StringReader(raw);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            Delimiters = [delimiter.ToString()],
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };

        string[]? header = null;
        var lines = new List<string>();

        while (!parser.EndOfData)
        {
            string[] fields;
            try
            {
                fields = parser.ReadFields() ?? [];
            }
            catch (MalformedLineException)
            {
                // Битую строку пропускаем, а не роняем весь импорт.
                continue;
            }

            if (fields.All(string.IsNullOrWhiteSpace)) continue;

            if (header is null)
            {
                header = fields;
                continue;
            }

            lines.Add(RenderRow(header, fields));
        }

        // Заголовок есть, а строк данных нет — отдаём хотя бы перечень колонок.
        if (lines.Count == 0 && header is not null)
            return "Columns: " + string.Join(", ", header.Where(h => !string.IsNullOrWhiteSpace(h)));

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static char DetectDelimiter(string raw)
    {
        var firstLineEnd = raw.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd > 0 ? raw[..firstLineEnd] : raw;

        var best = ',';
        var bestCount = 0;
        foreach (var d in CandidateDelimiters)
        {
            var count = firstLine.Count(c => c == d);
            if (count > bestCount)
            {
                bestCount = count;
                best = d;
            }
        }
        return best;
    }

    /// <summary>Сопоставляет ячейки с заголовками; пустые ячейки не упоминаются.</summary>
    internal static string RenderRow(string[] header, string[] fields)
    {
        // Если заголовков нет/меньше, чем ячеек — просто перечисляем значения.
        if (header.Length == 0)
            return string.Join(", ", fields.Where(f => !string.IsNullOrWhiteSpace(f)));

        var pairs = new List<string>(Math.Max(header.Length, fields.Length));
        var count = Math.Max(header.Length, fields.Length);
        for (var i = 0; i < count; i++)
        {
            var value = i < fields.Length ? fields[i]?.Trim() : null;
            if (string.IsNullOrWhiteSpace(value)) continue;

            var name = i < header.Length && !string.IsNullOrWhiteSpace(header[i])
                ? header[i]!.Trim()
                : $"Column{i + 1}";
            pairs.Add($"{name}: {value}");
        }
        return string.Join("; ", pairs);
    }
}
