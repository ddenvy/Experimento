using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>
/// Извлекает текст из .docx без сторонних зависимостей: docx — это ZIP,
/// основной текст лежит в word/document.xml. Каждый абзац (w:p) становится строкой;
/// содержимое таблиц проходит тем же путём, так как ячейки тоже содержат w:p.
/// </summary>
public static class DocxTextExtractor
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string Extract(Stream content)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("DOCX does not contain word/document.xml.");

        using var entryStream = entry.Open();
        var doc = XDocument.Load(entryStream);

        var sb = new StringBuilder();
        foreach (var paragraph in doc.Descendants(W + "p"))
        {
            var line = new StringBuilder();
            foreach (var node in paragraph.Descendants())
            {
                if (node.Name == W + "t")
                    line.Append(node.Value);
                else if (node.Name == W + "tab")
                    line.Append('\t');
                else if (node.Name == W + "br")
                    line.Append(' ');
            }

            var text = line.ToString().Trim();
            if (text.Length > 0) sb.AppendLine(text);
        }

        return sb.ToString().Trim();
    }
}
