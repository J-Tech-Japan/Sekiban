# Materialized-view binary consumer

This fixture is restored and compiled against the published `Sekiban.Dcb.MaterializedView` **10.14.0** package. The
compatibility proof then runs the already-built consumer without recompiling it after the current branch's
`Sekiban.Dcb.MaterializedView.dll` is copied into the output directory:

```sh
dotnet build dcb/tests/Sekiban.Dcb.MaterializedView.BinaryConsumer/Sekiban.Dcb.MaterializedView.BinaryConsumer.csproj -c Release
cp dcb/src/Sekiban.Dcb.MaterializedView/bin/Release/net9.0/Sekiban.Dcb.MaterializedView.dll \
  dcb/tests/Sekiban.Dcb.MaterializedView.BinaryConsumer/bin/Release/net9.0/
dotnet dcb/tests/Sekiban.Dcb.MaterializedView.BinaryConsumer/bin/Release/net9.0/Sekiban.Dcb.MaterializedView.BinaryConsumer.dll
```

The output must begin with `binary-consumer-ok:`. The final command deliberately does not invoke `dotnet build` or
`dotnet run`, so the process proves that the consumer was compiled against the real package reference and executed
against the current implementation as a binary consumer.
