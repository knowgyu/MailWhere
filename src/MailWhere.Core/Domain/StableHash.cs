using System.Security.Cryptography;
using System.Text;

namespace MailWhere.Core.Domain;

public static class StableHash
{
    public static string Create(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
