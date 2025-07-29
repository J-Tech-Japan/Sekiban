# クライアントAPI (Blazor) - Sekiban イベントソーシング

> **ナビゲーション**
> - [コア概念](01_core_concepts.md)
> - [はじめに](02_getting_started.md)
> - [アグリゲート、プロジェクター、コマンド、イベント](03_aggregate_command_events.md)
> - [複数アグリゲートプロジェクター](04_multiple_aggregate_projector.md)
> - [クエリ](05_query.md)
> - [ワークフロー](06_workflow.md)
> - [JSONとOrleansシリアライゼーション](07_json_orleans_serialization.md)
> - [API実装](08_api_implementation.md)
> - [クライアントAPI (Blazor)](09_client_api_blazor.md) (現在位置)
> - [Orleans設定](10_orleans_setup.md)
> - [Dapr設定](11_dapr_setup.md)
> - [ユニットテスト](12_unit_testing.md)
> - [よくある問題と解決策](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [バリューオブジェクト](15_value_object.md)
> - [デプロイメントガイド](16_deployment.md)

## Webフロントエンド実装

ドメインのWebフロントエンドを実装するには：

## APIクライアントの作成

まず、WebプロジェクトにAPIクライアントを作成します：

```csharp
public class YourApiClient(HttpClient httpClient)
{
    public async Task<YourQuery.ResultRecord[]> GetItemsAsync(
        CancellationToken cancellationToken = default)
    {
        List<YourQuery.ResultRecord>? items = null;

        await foreach (var item in httpClient.GetFromJsonAsAsyncEnumerable<YourQuery.ResultRecord>("/api/items", cancellationToken))
        {
            if (item is not null)
            {
                items ??= [];
                items.Add(item);
            }
        }

        return items?.ToArray() ?? [];
    }

    public async Task<CommandResponseSimple> CreateItemAsync(
        string param1,
        string param2,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateYourItemCommand(param1, param2);
        var response = await httpClient.PostAsJsonAsync("/api/createitem", command, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CommandResponseSimple>(cancellationToken: cancellationToken) 
            ?? new CommandResponseSimple();
    }
}
```

## APIクライアントの登録

Program.csでAPIクライアントを登録します：

```csharp
builder.Services.AddHttpClient<YourApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});
```

`https+http://`スキームは、サービス検出においてHTTPよりもHTTPSが優先されることを示しています。

## Blazorコンポーネントの作成

ドメインを表示して操作するためのBlazorコンポーネントを作成します：

```cshtml
@page "/items"
@using YourProject.Web.Data
@using YourProject.Domain.Aggregates.YourItem.Queries
@inject YourApiClient ApiClient

<h1>アイテム一覧</h1>

@if (items == null)
{
    <p><em>読み込み中...</em></p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>ID</th>
                <th>名前</th>
                <th>説明</th>
                <th>アクション</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var item in items)
            {
                <tr>
                    <td>@item.Id</td>
                    <td>@item.Name</td>
                    <td>@item.Description</td>
                    <td>
                        <button class="btn btn-primary" @onclick="() => EditItem(item)">編集</button>
                    </td>
                </tr>
            }
        </tbody>
    </table>

    <button class="btn btn-success" @onclick="ShowCreateForm">新規作成</button>
}

@if (showCreateForm)
{
    <div class="modal" tabindex="-1" style="display:block">
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">アイテム作成</h5>
                    <button type="button" class="btn-close" @onclick="HideCreateForm"></button>
                </div>
                <div class="modal-body">
                    <div class="mb-3">
                        <label for="name" class="form-label">名前</label>
                        <input type="text" class="form-control" id="name" @bind="newItemName">
                    </div>
                    <div class="mb-3">
                        <label for="description" class="form-label">説明</label>
                        <textarea class="form-control" id="description" @bind="newItemDescription"></textarea>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" @onclick="HideCreateForm">キャンセル</button>
                    <button type="button" class="btn btn-primary" @onclick="CreateItem">作成</button>
                </div>
            </div>
        </div>
    </div>
}

@code {
    private YourQuery.ResultRecord[]? items;
    private bool showCreateForm = false;
    private string? newItemName;
    private string? newItemDescription;
    private string? lastSortableUniqueId;

    protected override async Task OnInitializedAsync()
    {
        await LoadItems();
    }

    private async Task LoadItems()
    {
        // 一貫性のある読み取りを確保するためにlastSortableUniqueIdが利用可能であれば指定
        items = await ApiClient.GetItemsAsync();
    }

    private void ShowCreateForm()
    {
        showCreateForm = true;
        newItemName = null;
        newItemDescription = null;
    }

    private void HideCreateForm()
    {
        showCreateForm = false;
    }

    private async Task CreateItem()
    {
        if (!string.IsNullOrWhiteSpace(newItemName))
        {
            var response = await ApiClient.CreateItemAsync(newItemName, newItemDescription ?? "");
            lastSortableUniqueId = response.LastSortableUniqueId;
            HideCreateForm();
            await LoadItems();
        }
    }

    private void EditItem(YourQuery.ResultRecord item)
    {
        // 編集の実装
    }
}
```

## 一貫性のあるUIのためのIWaitForSortableUniqueIdの使用

コマンドの実行後にUIに最新のデータが表示されるようにするには、コマンド操作から返された`LastSortableUniqueId`を使用します：

```csharp
// APIクライアント
public async Task<YourQuery.ResultRecord[]> GetItemsAsync(
    string? waitForSortableUniqueId = null,
    CancellationToken cancellationToken = default)
{
    var uri = "/api/items";
    if (!string.IsNullOrEmpty(waitForSortableUniqueId))
    {
        uri += $"?waitForSortableUniqueId={Uri.EscapeDataString(waitForSortableUniqueId)}";
    }
    
    List<YourQuery.ResultRecord>? items = null;
    await foreach (var item in httpClient.GetFromJsonAsAsyncEnumerable<YourQuery.ResultRecord>(uri, cancellationToken))
    {
        items ??= [];
        if (item is not null)
        {
            items.Add(item);
        }
    }
    return items?.ToArray() ?? [];
}

// Blazorコンポーネントで
private async Task CreateItem()
{
    if (!string.IsNullOrWhiteSpace(newItemName))
    {
        var response = await ApiClient.CreateItemAsync(newItemName, newItemDescription ?? "");
        
        // 更新された状態が見えることを確認するためにLastSortableUniqueIdを使用
        await LoadItemsWithConsistency(response.LastSortableUniqueId);
        HideCreateForm();
    }
}

private async Task LoadItemsWithConsistency(string? sortableUniqueId)
{
    items = await ApiClient.GetItemsAsync(waitForSortableUniqueId: sortableUniqueId);
}
```

## 高度なUI機能

### ローディング状態

```cshtml
<button @onclick="CreateItem" disabled="@isProcessing" class="btn btn-primary">
    @if (isProcessing)
    {
        <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
        <span>処理中...</span>
    }
    else
    {
        <span>作成</span>
    }
</button>

@code {
    private bool isProcessing = false;
    
    private async Task CreateItem()
    {
        isProcessing = true;
        try
        {
            // コマンド実行
            var response = await ApiClient.CreateItemAsync(newItemName, newItemDescription ?? "");
            await LoadItemsWithConsistency(response.LastSortableUniqueId);
            HideCreateForm();
        }
        finally
        {
            isProcessing = false;
        }
    }
}
```

### エラーハンドリング

```csharp
public async Task<CommandResponseResult<CommandResponseSimple>> CreateItemAsync(
    string name,
    string description,
    CancellationToken cancellationToken = default)
{
    try
    {
        var command = new CreateYourItemCommand(name, description);
        var response = await httpClient.PostAsJsonAsync("/api/createitem", command, cancellationToken);
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<CommandResponseSimple>(cancellationToken: cancellationToken);
            return new CommandResponseResult<CommandResponseSimple>(result, null);
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return new CommandResponseResult<CommandResponseSimple>(null, new ApiError(
                (int)response.StatusCode,
                response.ReasonPhrase ?? "エラー",
                errorContent));
        }
    }
    catch (Exception ex)
    {
        return new CommandResponseResult<CommandResponseSimple>(null, new ApiError(0, "例外", ex.Message));
    }
}

public record CommandResponseResult<T>(T? Result, ApiError? Error)
{
    public bool IsSuccess => Error == null;
}

public record ApiError(int StatusCode, string Title, string Detail);
```

Blazorコンポーネントでは：

```cshtml
@if (!string.IsNullOrEmpty(errorMessage))
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        <strong>エラー:</strong> @errorMessage
        <button type="button" class="btn-close" @onclick="() => errorMessage = null"></button>
    </div>
}

@code {
    private string? errorMessage;
    
    private async Task CreateItem()
    {
        isProcessing = true;
        try
        {
            var response = await ApiClient.CreateItemAsync(newItemName, newItemDescription ?? "");
            if (response.IsSuccess)
            {
                await LoadItemsWithConsistency(response.Result!.LastSortableUniqueId);
                HideCreateForm();
                errorMessage = null;
            }
            else
            {
                errorMessage = response.Error!.Detail;
            }
        }
        finally
        {
            isProcessing = false;
        }
    }
}
```

## テスト用の依存性注入の使用

より良いテスタビリティのためにコンポーネントを依存性注入を使用する構造にします：

```csharp
// APIクライアントのインターフェースを定義
public interface IYourApiClient
{
    Task<YourQuery.ResultRecord[]> GetItemsAsync(string? waitForSortableUniqueId = null, CancellationToken cancellationToken = default);
    Task<CommandResponseResult<CommandResponseSimple>> CreateItemAsync(string name, string description, CancellationToken cancellationToken = default);
}

// インターフェースの実装
public class YourApiClient : IYourApiClient
{
    private readonly HttpClient _httpClient;
    
    public YourApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    
    // 実装
}

// Program.csで
builder.Services.AddHttpClient<IYourApiClient, YourApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});

// コンポーネントで
@inject IYourApiClient ApiClient
```

このアプローチにより、テスト目的でAPIクライアントをモック化できます。