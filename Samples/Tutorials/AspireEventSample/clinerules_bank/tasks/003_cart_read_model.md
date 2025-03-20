イベントのリードモデルが無事にできました。
AspireEventSample.ReadModels/BranchDbContext.cs
に
BranchDbRecord
でBranchの情報を保存できています。

同じように、
AspireEventSample.ApiService/Grains/ICartEntityWriter.cs
を Postgres対応したいです。

AspireEventSample.ReadModels/BranchDbContext.cs
に追加するために、
CartDbRecordを作成してください。
ただ、
AspireEventSample.ApiService/Grains/ICartEntityWriter.cs
は変更せずに
AspireEventSample.ApiService/Grains/IBranchEntityPostgresWriter.cs
の時と同じように別のインターフェース、別の実装を作ってください。
そして、
AspireEventSample.ApiService/Grains/EventConsumerGrain.cs
から、Postgres用の機能を実行してください。