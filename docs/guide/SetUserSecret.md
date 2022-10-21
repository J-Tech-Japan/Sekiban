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


# 基本の追加方法
## WebAPI
dotnet user-secrets init 
のコマンドをシークレットが必要な各フォルダで行う

## Test
dotnet user-secrets init
のコマンドをシークレットが必要なプロジェクトで行う

以下のような、ConfigurationBuilderを定義する。AddUserSecretを行うために

Microsoft.Extensions.Configuration.UserSecrets package

をNugetで追加する必要がある。

以下のようなTestBaseを使うことにより、対応可能
```
[Collection("Sequential")]
public class TestBase : IClassFixture<TestBase.SekibanTestFixture>, IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    protected readonly SekibanTestFixture _sekibanTestFixture;

    public class SekibanTestFixture : ISekibanTestFixture
    {
        public SekibanTestFixture()
        {
            var builder = new ConfigurationBuilder().SetBasePath(PlatformServices.Default.Application.ApplicationBasePath)
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables()
                .AddUserSecrets(Assembly.GetExecutingAssembly());
            Configuration = builder.Build();
        }
        public IConfigurationRoot Configuration { get; set; }
    }

    public TestBase(
        SekibanTestFixture sekibanTestFixture,
        bool inMemory = false,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        _sekibanTestFixture = sekibanTestFixture;
        _serviceProvider = DependencyHelper.CreateDefaultProvider(sekibanTestFixture, inMemory, null, multipleProjectionType);
    }
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing) { }
    }
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
```





