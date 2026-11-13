using CompatibilityDomain;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

if (Environment.GetEnvironmentVariable("DOTNET_ROLL_FORWARD") is not null || Environment.Version.Major != 9 ||
    typeof(JsonSerializer).Assembly.GetName().Version?.Major != 9)
    throw new InvalidOperationException("Fixtures require .NET 9 and System.Text.Json 9, with DOTNET_ROLL_FORWARD unset.");
Console.WriteLine(RuntimeInformation.FrameworkDescription);
Console.WriteLine($"System.Text.Json {typeof(JsonSerializer).Assembly.GetName().Version}");
var directory = Path.GetFullPath(args.Single());
Directory.CreateDirectory(directory);
foreach (var (name, bytes) in await FixedInputs.SerializeAll())
{
    var path = Path.Combine(directory, name);
    if (File.Exists(path)) throw new InvalidOperationException($"Refusing to overwrite baseline fixture {path}");
    await File.WriteAllBytesAsync(path, bytes);
    Console.WriteLine($"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}  {name}");
}
