# キー情報などの秘匿情報に関して

https://learn.microsoft.com/ja-jp/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=linux#secret-manager

上記のマイクロソフトのサイトで書かれている方法で秘匿情報を管理します

## CustomerWebApi

```appsettings.json

{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Sekiban": {
    "Default": {
      "AggregateEventCosmosDbContainer": "testevents",
      "AggregateEventCosmosDbContainerDissolvable": "dissolvableevents",
      "CosmosDbEndPointUrl": "https://[YOUR COSMOS ENDPOINT URL].documents.azure.com:443/",
      "CosmosDbAuthorizationKey": "[Set your cosmos db key here]",
      "CosmosDbDatabase": "CustomerDb",
      "CosmosDbContainer": "testitems",
      "CosmosDbContainerDissolvable": "dissolvableitems",
      "Aggregates": {
        "UseHybridDefault": true,
        "TakeSnapshotDefault": true,
        "SnapshotFrequencyDefault": 80,
        "SnapshotOffsetDefault": 15,
        "UseUpdateMarker": true
      }
    }
  },
  "AllowedHosts": "*"
}

```
appsettings.json にふさわしい値を入力します。
AggregateEventCosmosDbContainer : イベントのコンテナ
AggregateEventCosmosDbContainerDissolvable : 一時的イベントのコンテナ
CosmosDbDatabase : CosmosDBのデータベース名
CosmosDbContainer : コマンド、スナップショットのコンテナ
CosmosDbContainerDissolvable : 一時的コマンド、スナップショットのコンテナ

### 秘匿情報に関しては、シークレットマネージャツールを使用して、設定します
```zsh
-- CustomerWebApiのプロジェクトフォルダへ移動
cd samples/CustomerWebApi

-- CosmosDbEndPointUrlを設定
dotnet user-secrets set "Sekiban:Default:CosmosDbEndPointUrl" "https://******.documents.azure.com:443/"

-- CosmosDbAuthorizationKeyを設定
dotnet user-secrets set "Sekiban:Default:CosmosDbAuthorizationKey" "******"

```

## CustomerWithTenantAddonWebApi 
- appsettngs.jsonは CustomerWebApiと同様
- シークレットマネージャはCustomerWithTenantAddonWebApiフォルダに移動してからは同じ

```zsh
-- CustomerWebApiのプロジェクトフォルダへ移動
cd samples/CustomerWithTenantAddonWebApi

-- CosmosDbEndPointUrlを設定
dotnet user-secrets set "Sekiban:Default:CosmosDbEndPointUrl" "https://******.documents.azure.com:443/"

-- CosmosDbAuthorizationKeyを設定
dotnet user-secrets set "Sekiban:Default:CosmosDbAuthorizationKey" "******"

```

## SampleProjectStoryXTest

- appsettings.jsonは、DefaultとSecondary両方設定する
- データベースは同じでも良いし別でも良い
- コンテナは、DefaultとSecondary別にする
- シークレットマネージャはCustomerWithTenantAddonWebApiフォルダに移動してからは同じ


```zsh
-- CustomerWebApiのプロジェクトフォルダへ移動
cd test/SampleProjectStoryXTest

-- CosmosDbEndPointUrlを設定 Default
dotnet user-secrets set "Sekiban:Default:CosmosDbEndPointUrl" "https://******.documents.azure.com:443/"

-- CosmosDbAuthorizationKeyを設定 Default
dotnet user-secrets set "Sekiban:Default:CosmosDbAuthorizationKey" "******"

-- CosmosDbEndPointUrlを設定 Secondary
dotnet user-secrets set "Sekiban:Secondary:CosmosDbEndPointUrl" "https://******.documents.azure.com:443/"

-- CosmosDbAuthorizationKeyを設定 Secondary
dotnet user-secrets set "Sekiban:Secondary:CosmosDbAuthorizationKey" "******"

```





