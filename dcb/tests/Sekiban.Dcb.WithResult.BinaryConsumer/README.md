# WithResult binary consumer

This fixture is restored and compiled against the published `Sekiban.Dcb.WithResult` **10.14.0** package. The
already-built consumer is then executed after the current branch's `Sekiban.Dcb.WithResult.dll`, core model, and
WithResult model assemblies are copied into its output directory; the execution command does not rebuild or restore.

```sh
dotnet build dcb/tests/Sekiban.Dcb.WithResult.BinaryConsumer/Sekiban.Dcb.WithResult.BinaryConsumer.csproj -c Release
dotnet build dcb/src/Sekiban.Dcb.WithResult/Sekiban.Dcb.WithResult.csproj -c Release -f net9.0
cp dcb/src/Sekiban.Dcb.WithResult/bin/Release/net9.0/Sekiban.Dcb.WithResult.dll \
  dcb/tests/Sekiban.Dcb.WithResult.BinaryConsumer/bin/Release/net9.0/
cp dcb/src/Sekiban.Dcb.Core/bin/Release/net9.0/Sekiban.Dcb.Core.dll \
  dcb/tests/Sekiban.Dcb.WithResult.BinaryConsumer/bin/Release/net9.0/
cp dcb/src/Sekiban.Dcb.Core.Model/bin/Release/net9.0/Sekiban.Dcb.Core.Model.dll \
  dcb/tests/Sekiban.Dcb.WithResult.BinaryConsumer/bin/Release/net9.0/
cp dcb/src/Sekiban.Dcb.WithResult.Model/bin/Release/net9.0/Sekiban.Dcb.WithResult.Model.dll \
  dcb/tests/Sekiban.Dcb.WithResult.BinaryConsumer/bin/Release/net9.0/
dotnet dcb/tests/Sekiban.Dcb.WithResult.BinaryConsumer/bin/Release/net9.0/Sekiban.Dcb.WithResult.BinaryConsumer.dll
```

The output must begin with `with-result-binary-consumer-ok:`. The final command deliberately invokes the existing
consumer DLL directly, proving execution without recompilation.
