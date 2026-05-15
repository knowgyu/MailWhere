namespace MailWhere.Core.Domain;

public static class EvidencePolicy
{
    public const int MaxEvidenceChars = 240;

    public static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= MaxEvidenceChars
            ? normalized
            : normalized[..MaxEvidenceChars].TrimEnd() + "…";
    }
}
