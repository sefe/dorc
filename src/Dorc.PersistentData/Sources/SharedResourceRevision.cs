using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dorc.PersistentData.Sources;

public static class SharedResourceRevision
{
    public static string ForFields(params string?[] fields) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fields))));
}
