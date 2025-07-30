C#の
src/Sekiban.Pure/Projectors/IMultiProjector.cs
を typescript で正しく実装したい。

そのために、

src/Sekiban.Pure/Projectors/AggregateListProjector.cs

を
/Users/tomohisa/dev/GitHub/Sekiban-ts/ts/src/packages/core/src/queries
に実装してください。
純粋にイベントを古いものから与えたら、現在のMultiProjectionStateを作成するというものです。

その後、

IMultiProjectorTypes
をschema firstで組み立てる機能があると思いますが、そこも

src/Sekiban.Pure.SourceGenerator/MultiProjectorTypesGenerator.cs

を参考に作ってください。

ts/src/packages/core/src/schema-registry/schema-domain-types.ts

ここに必要です。

ここまでできたら、実行することは考えずに、上記のC#の機能を使って、
ts/samples/dapr-sample/packages/domain/src/domain-types.ts
ここで、
task user のaggregate list projector を登録できるようにしてください。
その後、

internalUsages/SharedDomain/Aggregates/WeatherForecasts/Queries/WeatherForecastQuery.cs
このように、Taskを複数取得できる、AggregateListProjectorをベースにしたリストクエリーを作り、
ts/samples/dapr-sample/packages/domain/src/domain-types.ts
にも登録できるようにしてください。

クエリを登録できるためには、
src/Sekiban.Pure/Query/IQueryTypes.cs
の機能もtypescriptに必要で、
ts/src/packages/core/src/schema-registry/schema-domain-types.ts
に登録機能を作りつつ、

ts/samples/dapr-sample/packages/domain/src/domain-types.ts

ここで登録することも必要です。