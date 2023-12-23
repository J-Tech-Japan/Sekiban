# 早速始めましょう

## 実験してみよう。

迅速に始めるために、まずはすでに設定されたプロジェクトを使ってsekibanをテストしてみることをおすすめします。

リポジトリ内の`/Samples/Tutorials/1.GetStarted` フォルダにあります。

### GetStarted Solutionを開く。

Get Started .NET 7で作成されています。試す方法は複数あります。

1. Cosmos DBを使用  [Cosmos DBを使って試す](./test-out-cosmos.md)
2. Dynamo DBを使用  [Dynamo DBを使って試す](./test-out-dynamo.md)


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


これらは基本的なプロジェクト設定です。詳細情報は [Sekiban Event Sourcing Basics](./sekiban-event-sourcing-basics.md) でフォローされます。
