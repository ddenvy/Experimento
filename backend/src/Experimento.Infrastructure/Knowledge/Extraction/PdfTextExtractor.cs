using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>Извлекает текстовый слой PDF через PdfPig. Сканированные PDF без текстового слоя дадут пустой результат.</summary>
public static class PdfTextExtractor
{
    public static string Extract(Stream content)
    {
        using var document = PdfDocument.Open(content);
        var sb = new StringBuilder();

        foreach (Page page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text.Trim());
        }

        return sb.ToString().Trim();
    }
}
