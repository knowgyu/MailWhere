using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MailWhere.Core.Search;

public static class MailMirrorText
{
    private static readonly Regex ManyBlankLines = new("\\n{3,}", RegexOptions.Compiled);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (ch != '\0')
            {
                builder.Append(ch);
            }
        }

        return ManyBlankLines.Replace(builder.ToString().Trim(), "\n\n");
    }

    public static string Hash(string? value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(value)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
