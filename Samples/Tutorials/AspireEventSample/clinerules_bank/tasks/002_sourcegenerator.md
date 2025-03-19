AspireEventSample.ReadModels/BranchDbRecord.cs と
AspireEventSample.ApiService/Aggregates/ReadModel/BranchEntity.cs
は同じ内容です。もし、BranchDbRecord に [GenerateSerializer] を書いて、Orleansで使えるようにしても問題無いのであればそうしたいです。一度
AspireEventSample.MigrationHost/Migrations 
フォルダのマイグレーションを削除して、新規で[GenerateSerializer] を書いてもマイグレーションが使えるか試してみてください。
---
## 追加依頼 1

AspireEventSample.ApiService/Aggregates/ReadModel/BranchEntity.cs
に書いている [Id(0)]のようなものを

AspireEventSample.ReadModels/BranchDbRecord.cs 
にも書いてください。

そして
AspireEventSample.ApiService/Aggregates/ReadModel/BranchEntity.cs
を削除して、

AspireEventSample.ApiService/Aggregates/ReadModel/BranchEntity.cs
の代わりに

AspireEventSample.ReadModels/BranchDbRecord.cs 

を使ってください。

