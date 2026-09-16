using Experimento.Infrastructure.Knowledge.Extraction;
using Xunit;

namespace Experimento.Infrastructure.Tests;

public class DocumentTextExtractorTests
{
    private readonly DocumentTextExtractor _sut = new();

    [Fact]
    public void SupportedExtensions_covers_all_advertised_formats()
    {
        foreach (var ext in new[] { ".txt", ".md", ".csv", ".xls", ".xlsx", ".docx", ".pdf" })
            Assert.Contains(ext, _sut.SupportedExtensions);
        Assert.Equal(7, _sut.SupportedExtensions.Count);
    }

    [Fact]
    public async Task Unsupported_extension_throws()
    {
        using var stream = new MemoryStream("x"u8.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractAsync("archive.zip", stream));
    }

    [Fact]
    public async Task Txt_extracts_utf8_and_strips_bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("hello world"u8.ToArray()).ToArray();
        using var stream = new MemoryStream(bytes);
        Assert.Equal("hello world", await _sut.ExtractAsync("note.txt", stream));
    }

    [Fact]
    public void Csv_comma_header_rows_become_header_value_pairs()
    {
        var csv = "name,cas,solubility\nAspirin,50-78-2,3 mg/mL\nCaffeine,58-08-2,20 mg/mL\n"u8.ToArray();
        var result = CsvTableExtractor.Extract(csv);

        Assert.Contains("name: Aspirin; cas: 50-78-2; solubility: 3 mg/mL", result);
        Assert.Contains("name: Caffeine; cas: 58-08-2; solubility: 20 mg/mL", result);
        // Сырого заголовка в результате быть не должно.
        Assert.DoesNotContain("name,cas", result);
    }

    [Fact]
    public void Csv_semicolon_and_quoted_fields_are_supported()
    {
        // Точка с запятой + кавычки по RFC 4180 с запятой внутри поля.
        var csv = "product;note\r\nAspirin;\"tablet, 100 mg\"\r\n"u8.ToArray();
        var result = CsvTableExtractor.Extract(csv);
        Assert.Equal("product: Aspirin; note: tablet, 100 mg", result);
    }

    [Fact]
    public void Csv_windows1251_cyrillic_is_decoded_via_fallback()
    {
        // "Аспирин" в Windows-1251; заголовок ASCII, данные — кириллица в cp1251.
        var header = "name;value\r\n"u8.ToArray();
        var win1251 = new byte[] { 0xC0, 0xF1, 0xEF, 0xE8, 0xF0, 0xE8, 0xED };
        var csv = header.Concat(win1251).Concat(new byte[] { (byte)';', (byte)'1' }).ToArray();
        var result = CsvTableExtractor.Extract(csv);
        Assert.Equal("name: Аспирин; value: 1", result);
    }

    [Fact]
    public void Csv_header_only_lists_columns()
    {
        var csv = "a,b,c\n"u8.ToArray();
        Assert.Equal("Columns: a, b, c", CsvTableExtractor.Extract(csv));
    }

    [Fact]
    public void Csv_empty_cells_are_skipped_in_rows()
    {
        var csv = "name,cas,formula\nAspirin,50-78-2,\n"u8.ToArray();
        var result = CsvTableExtractor.Extract(csv);
        Assert.Equal("name: Aspirin; cas: 50-78-2", result);
        Assert.DoesNotContain("formula:", result);
    }

    [Fact]
    public async Task Docx_extracts_paragraph_text()
    {
        // Минимальный валидный DOCX: ZIP с word/document.xml из двух абзацев.
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            await using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write("""
<?xml version="1.0"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p><w:r><w:t>Aspirin solubility</w:t></w:r></w:p>
    <w:p><w:r><w:t>3 mg per mL</w:t></w:r></w:p>
  </w:body>
</w:document>
""");
            }
        }
        ms.Position = 0;

        var result = await _sut.ExtractAsync("paper.docx", ms);
        Assert.Equal($"Aspirin solubility{Environment.NewLine}3 mg per mL", result);
    }
}
