using System.Text;

namespace Experimento.Infrastructure.Knowledge.Extraction;

/// <summary>Простые текстовые форматы: .txt и .md.</summary>
public static class PlainTextExtractor
{
    public static string Extract(byte[] bytes)
    {
        var encoding = TextEncodingDetector.Detect(bytes);
        // Текстовые файлы иногда приходят с BOM (U+FEFF) или завершающим null — убираем их.
        const char Bom = (char)0xFEFF;
        return encoding.GetString(bytes)
            .Replace("\0", "")
            .TrimStart(Bom)
            .Trim();
    }
}

/// <summary>
/// Определение кодировки по BOM с фолбэком: строгий UTF-8, затем Windows-1251
/// (типичная кодировка CSV, выгруженных Excel в русской локали).
/// </summary>
internal static class TextEncodingDetector
{
    private static readonly Encoding Windows1251;

    static TextEncodingDetector()
    {
        // На .NET Core кодировки старых кодовых страниц доступны только после регистрации провайдера.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1251 = Encoding.GetEncoding(1251);
    }

    public static Encoding Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            _ = strictUtf8.GetString(bytes);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Windows1251;
        }
    }
}
