# 早速始めましょう

## 実験してみよう。

迅速に始めるために、まずはすでに設定されたプロジェクトを使ってsekibanをテストしてみることをおすすめします。

リポジトリ内の`/Samples/Tutorials/1.GetStarted` フォルダにあります。

### GetStarted Solutionを開く。

Get Started .NET 8で作成されています。試す方法は複数あります。

1. Cosmos DBを使用  [Cosmos DBを使って試す](./test-out-cosmos.md)
2. Dynamo DBを使用  [Dynamo DBを使って試す](./test-out-dynamo.md)
3. PostgreSQLを使用 [PostgreSQLを使って試す](./test-out-postgres.md)

### 自身のSekiban Projectを作成する。

基本的に、自分のプロジェクトを作成するためには3つのプロジェクトが必要です。

自分のプロジェクトを作成するためには、`/Samples/Tutorials/1.GetStarted` フォルダ内のチュートリアルプロジェクトを参考にできます。

1. Domain Project。このプロジェクトは通常、Aggregate、Command、Event、Projection、Queryなどのコアなイベントソーシングの内容のみを含みます。

    Domain Projectは`Sekiban.Core` Nugetパッケージを追加します。

    Domain Projectは Aggregate、Command、Event、Projection、Queryなどを含みます。

    Domain Projectには Domain Dependency Definitionが含まれています。

2. Test Project。このプロジェクトはDomain Projectをテストします。

    Test Projectは Domain Projectを参照します。

    Test Projectは Sekiban.Testing Nugetパッケージを追加します。

    Test Projectには Aggregate Test および/または Unified Testが含まれています。

3. Executing Project。このプロジェクトはコンソールアプリケーション、Webアプリケーション、ファンクションアプリケーション、またはその他の実行形式のアプリケーションです。多くの場合、Domain ProjectにアクセスするためのWeb APIインターフェースになります。

    Executing Projectは Domain Projectを参照します。

    Executing Projectは Infrastructure projectを追加します。現在、Sekibanは Azure 
    Cosmos DB および AWS Dynamo DBをサポートしています。
    
    Executing ProjectがAzure Cosmos DBを使用する場合は、`Sekiban.Infrastructure.Cosmos` Nugetパッケージを追加します。
    
    Executing ProjectがAWS Dynamo DBを使用する場合は、`Sekiban.Infrastructure.Dynamo` Nugetパッケージを追加します。
    
    Executing Projectが Sekiban.Web API Generatorを使用したWeb API Projectとなる場合は、以下の項目が含まれます。

    - Sekiban.Web Nugetパッケージを追加します。
    - IWebDependencyDefinitionを継承した Web Dependency Definition。
    - Program.csは Sekiban.Core 設定として`AddSekibanCoreWithDependency`を持つ。
    - Program.csはインフラ設定として`AddSekibanCosmosDB`を持つ。
    - Program.csは Web 設定として`AddSekibanWeb`を持つ。

#### Azure Cosmos DBのためのappsettings.json
以下はCosmos DBの最小設定です。Cosmos DBの接続文字列はCosmos DBのウェブサイトまたはAzure CLIから取得できます。`CosmosDbDatabase`は使用するデータベース名であるべきです。コンテナ名はappsettingsで定義できますが、イベントのために`events`、コマンドとスナップショットのために`items`を使用します。
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings" : {
    "SekibanCosmos" : "[Set your cosmos db connection string]",
    "SekibanBlob": "[Set your blob connection string here. (not necessary for just running the sample)]"
  }
}
```

このシンプルな設定方法により、sekibanは以下のCosmos Dbインスタンスに接続します。
- Azure Cosmos DBエンドポイント： "ConnectionStrings:SekibanCosmos"に設定したもの
- Azure Cosmos DBデータベース： デフォルトのデータベース名は"SekibanDb"、またはappsettingsの"Sekiban:Default:Azure:CosmosDatabase"で設定できます
- Azure Cosmos DBコンテナ： 2つのコンテナが必要です。
  - 1. "events"コンテナ。全てのイベントを保存します。
  - 2. "items"コンテナ。

以下の画像のようになります。
![Cosmos DB](../images/quick-start/image1.png)


#### Dynamo DBのためのappsettings.json
以下はDynamo DBの最小設定です。テーブル名 `DynamoEventsTable` `DynamoItemsTable` はappsettingsで定義できますが、イベントのために`events`、コマンドとスナップショットのために`items`を使用します。
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Sekiban": {
    "Default": {
      "Aws" : {
        "DynamoRegion": "us-east-1",
        "AccessKeyId": "[Set your dynamo db access id here]",
        "AccessKey": "[Set your dynamo db access key here]",
        "DynamoItemsTable": "jjlt_items",
        "DynamoEventsTable": "jjlt_events",
        "S3BucketName": "jjlt-s3",
        "S3Region": "us-west-1"
      }
    }
  }
}
```


これらは基本的なプロジェクト設定です。詳細情報は [Sekiban Event Sourcing Basics](./sekiban-event-sourcing-basics.md) でフォローされます。
