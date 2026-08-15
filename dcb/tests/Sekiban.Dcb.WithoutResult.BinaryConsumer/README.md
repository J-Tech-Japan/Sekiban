# WithoutResult binary consumer

This fixture is restored and compiled against the published `Sekiban.Dcb.WithoutResult` **10.14.0** package. The
already-built consumer is then executed after the current branch's `Sekiban.Dcb.WithoutResult.dll`, Core, and
WithoutResult model assemblies are copied into its output directory; the execution command does not rebuild or restore.

```sh
dotnet build dcb/tests/Sekiban.Dcb.WithoutResult.BinaryConsumer/Sekiban.Dcb.WithoutResult.BinaryConsumer.csproj -c Release
dotnet build dcb/src/Sekiban.Dcb.WithoutResult/Sekiban.Dcb.WithoutResult.csproj -c Release -f net9.0
cp dcb/src/Sekiban.Dcb.WithoutResult/bin/Release/net9.0/Sekiban.Dcb.WithoutResult.dll \
  dcb/tests/Sekiban.Dcb.WithoutResult.BinaryConsumer/bin/Release/net9.0/
cp dcb/src/Sekiban.Dcb.Core/bin/Release/net9.0/Sekiban.Dcb.Core.dll \
  dcb/tests/Sekiban.Dcb.WithoutResult.BinaryConsumer/bin/Release/net9.0/
cp dcb/src/Sekiban.Dcb.Core.Model/bin/Release/net9.0/Sekiban.Dcb.Core.Model.dll \
  dcb/tests/Sekiban.Dcb.WithoutResult.BinaryConsumer/bin/Release/net9.0/
cp dcb/src/Sekiban.Dcb.WithoutResult.Model/bin/Release/net9.0/Sekiban.Dcb.WithoutResult.Model.dll \
  dcb/tests/Sekiban.Dcb.WithoutResult.BinaryConsumer/bin/Release/net9.0/
dotnet dcb/tests/Sekiban.Dcb.WithoutResult.BinaryConsumer/bin/Release/net9.0/Sekiban.Dcb.WithoutResult.BinaryConsumer.dll
```

The output must begin with `without-result-binary-consumer-ok:`. The final command deliberately invokes the existing
consumer DLL directly, proving execution without recompilation.
