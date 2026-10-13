using System.Globalization;
using System.Text;

namespace MS_Application.Helpers;

public static class TextSearchHelper
{
    /// <summary>
    /// Chữ thường + bỏ dấu, khớp với lower(unaccent(...)) của cột dist.songs.search_text.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var decomposed = input.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            sb.Append(c switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => c
            });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// Pattern "%keyword%" cho LIKE, đã escape ký tự đặc biệt.
    /// </summary>
    public static string ToContainsPattern(string? keyword)
    {
        var escaped = Normalize(keyword)
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{escaped}%";
    }
}
