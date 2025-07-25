ts/samples/dapr-sample/packages/domain/src/aggregates/task/queries/task-list-query.ts
でクエリができたので、

ts/samples/dapr-sample/packages/api/src/routes/task-routes.ts#L105

の task list をこのクエリを実行する形にしてください。

これを行っていくにあたり、
ts/src/packages/dapr/src/actors/multi-projector-actor.ts
の内部実装が、C#と同じく、
1. イベントを、イベントストアおよびイベント配信から受け取る
2. 内部的に、safe state と unsafe state を管理するが、その際に、
ts/src/packages/core/src/schema-registry/schema-domain-types.ts
のIMultiProjectorTypesを利用してプロジェクションを行い管理する

行って、C#のやり方を完全にトレースしてください。
src/Sekiban.Pure.Dapr/Actors/MultiProjectorActor.cs
新たなやり方は不要です。

tattletale-reporter は、C#の行ない方をちゃんと行っているか、シンプルな代案を作っていないか、嘘ついていないかをチェック報告してください。
typescript-build-tester　が正しい実行の仕方を管理しつつ、動作テストまで行ってください。