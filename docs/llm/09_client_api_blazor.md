# Client API (Blazor) - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md) (You are here)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)

## Web Frontend Implementation

To implement a web frontend for your domain:

## Creating an API Client

First, create an API client in the Web project:

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

## Register the API Client

Register the API client in Program.cs:

```csharp
builder.Services.AddHttpClient<YourApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});
```

The `https+http://` scheme indicates that HTTPS is preferred over HTTP for service discovery.

## Create Blazor Components

Create a Blazor component to display and interact with your domain:

```cshtml
@page "/items"
@using YourProject.Web.Data
@using YourProject.Domain.Aggregates.YourItem.Queries
@inject YourApiClient ApiClient

<h1>Your Items</h1>

@if (items == null)
{
    <p><em>Loading...</em></p>
}
else
{
    <table class="table">
        <thead>
            <tr>
                <th>ID</th>
                <th>Name</th>
                <th>Description</th>
                <th>Actions</th>
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
                        <button class="btn btn-primary" @onclick="() => EditItem(item)">Edit</button>
                    </td>
                </tr>
            }
        </tbody>
    </table>

    <button class="btn btn-success" @onclick="ShowCreateForm">Create New Item</button>
}

@if (showCreateForm)
{
    <div class="modal" tabindex="-1" style="display:block">
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Create Item</h5>
                    <button type="button" class="btn-close" @onclick="HideCreateForm"></button>
                </div>
                <div class="modal-body">
                    <div class="mb-3">
                        <label for="name" class="form-label">Name</label>
                        <input type="text" class="form-control" id="name" @bind="newItemName">
                    </div>
                    <div class="mb-3">
                        <label for="description" class="form-label">Description</label>
                        <textarea class="form-control" id="description" @bind="newItemDescription"></textarea>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" @onclick="HideCreateForm">Cancel</button>
                    <button type="button" class="btn btn-primary" @onclick="CreateItem">Create</button>
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
        // Specify lastSortableUniqueId if available to ensure consistent reads
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
        // Implementation for editing
    }
}
```

## Using IWaitForSortableUniqueId for Consistent UI

To ensure that your UI shows the most recent data after performing a command, use the `LastSortableUniqueId` returned from command operations:

```csharp
// API Client
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

// In the Blazor component
private async Task CreateItem()
{
    if (!string.IsNullOrWhiteSpace(newItemName))
    {
        var response = await ApiClient.CreateItemAsync(newItemName, newItemDescription ?? "");
        
        // Use the LastSortableUniqueId to ensure we see the updated state
        await LoadItemsWithConsistency(response.LastSortableUniqueId);
        HideCreateForm();
    }
}

private async Task LoadItemsWithConsistency(string? sortableUniqueId)
{
    items = await ApiClient.GetItemsAsync(waitForSortableUniqueId: sortableUniqueId);
}
```

## Advanced UI Features

### Loading States

```cshtml
<button @onclick="CreateItem" disabled="@isProcessing" class="btn btn-primary">
    @if (isProcessing)
    {
        <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
        <span>Processing...</span>
    }
    else
    {
        <span>Create</span>
    }
</button>

@code {
    private bool isProcessing = false;
    
    private async Task CreateItem()
    {
        isProcessing = true;
        try
        {
            // Command execution
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

### Error Handling

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
                response.ReasonPhrase ?? "Error",
                errorContent));
        }
    }
    catch (Exception ex)
    {
        return new CommandResponseResult<CommandResponseSimple>(null, new ApiError(0, "Exception", ex.Message));
    }
}

public record CommandResponseResult<T>(T? Result, ApiError? Error)
{
    public bool IsSuccess => Error == null;
}

public record ApiError(int StatusCode, string Title, string Detail);
```

In your Blazor component:

```cshtml
@if (!string.IsNullOrEmpty(errorMessage))
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        <strong>Error:</strong> @errorMessage
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

## Using Dependency Injection for Testing

Structure your components to use dependency injection for better testability:

```csharp
// Define an interface for your API client
public interface IYourApiClient
{
    Task<YourQuery.ResultRecord[]> GetItemsAsync(string? waitForSortableUniqueId = null, CancellationToken cancellationToken = default);
    Task<CommandResponseResult<CommandResponseSimple>> CreateItemAsync(string name, string description, CancellationToken cancellationToken = default);
}

// Implement the interface
public class YourApiClient : IYourApiClient
{
    private readonly HttpClient _httpClient;
    
    public YourApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    
    // Implementation
}

// In Program.cs
builder.Services.AddHttpClient<IYourApiClient, YourApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});

// In your component
@inject IYourApiClient ApiClient
```

This approach allows you to mock the API client for testing purposes.
