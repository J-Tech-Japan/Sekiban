using System.Reflection;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Test.IndexedDb;

public class NodeJsRuntime : ISekibanJsRuntime
{
    private static string LibNodeName => Environment.OSVersion.Platform switch
    {
        // Windows
        PlatformID.Win32NT => "libnode.dll",

        // MacOS and Linux
        PlatformID.Unix => "libnode.so.115",

        _ => throw new PlatformNotSupportedException(),
    };

    private static readonly string basedir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    private static readonly NodejsPlatform nodejsPlatform = new(Path.Combine(basedir, "Assets", LibNodeName));
    private static readonly NodejsEnvironment nodejsEnvironment = nodejsPlatform.CreateEnvironment(basedir);

    public async Task<ISekibanIndexedDbContext> CreateContextAsync(string context)
    {
        await nodejsEnvironment.RunAsync(async () => await nodejsEnvironment.ImportAsync("./Assets/indexed-db.mjs", esModule: true));

        var store = await nodejsEnvironment.RunAsync(async () =>
        {
            var module = await nodejsEnvironment.ImportAsync(ISekibanJsRuntime.RuntimePath, property: "init", esModule: true);

            var store = await module
                .Call(JSValue.Undefined, context)
                .CastTo<JSPromise>()
                .AsTask();

            return new JSReference(store);
        });

        return new NodeIndexedDbContext(nodejsEnvironment, store);
    }
}
