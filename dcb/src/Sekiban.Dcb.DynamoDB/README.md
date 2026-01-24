# Sekiban.Dcb.DynamoDB

Amazon DynamoDB event store provider for Sekiban DCB (Dynamic Consistency Boundary).

📚 **Full Documentation**: [sekiban.dev](https://www.sekiban.dev/)

## Sekiban Implementations

| Implementation | Status |
|---------------|--------|
| **Sekiban DCB** | ✅ Recommended |
| Sekiban.Pure | ⚠️ Deprecated |

## Installation

```bash
dotnet add package Sekiban.Dcb.DynamoDB
```

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

## Related Packages

- `Sekiban.Dcb.BlobStorage.S3` - S3 snapshot storage for large projections

## License

Apache 2.0 - Copyright (c) 2022- J-Tech Japan
