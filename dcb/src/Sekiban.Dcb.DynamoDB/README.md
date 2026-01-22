# Sekiban.Dcb.DynamoDB

Amazon DynamoDB provider for Sekiban DCB.

## Usage

```csharp
// Program.cs
services.AddSekibanDcbDynamoDb(configuration);
```

### appsettings.json

```json
{
  "AWS": {
    "Region": "ap-northeast-1"
  },
  "DynamoDb": {
    "EventsTableName": "SekibanEvents",
    "TagsTableName": "SekibanTags",
    "ProjectionStatesTableName": "SekibanProjectionStates",
    "ServiceUrl": null
  }
}
```

### Local DynamoDB / LocalStack

```json
{
  "DynamoDb": {
    "ServiceUrl": "http://localhost:8000"
  }
}
```

