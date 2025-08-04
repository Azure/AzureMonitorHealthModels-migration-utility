using System.Security.Cryptography;
using System.Text;

namespace Microsoft.CloudHealth.PreviewMigration;

public static class Utils
{
    public static Guid GenerateDeterministicGuid(this string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);
        var guidBytes = new byte[16];
        Array.Copy(hashBytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
}