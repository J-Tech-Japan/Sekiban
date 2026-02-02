## Summary
- CosmosDB の legacy events/tags を JSONL 退避 → コンテナ再作成(/pk) → 新形式へ変換アップロードするツールを追加。

## Changes
- `tools/MigrateDcbCosmosEventsTags` を新規追加（export/delete/recreate/import を一括実行）。
- 変換ロジックで `serviceId`/`pk`/`timestamp`/`createdAt` の欠落を補完し、payload/tags を正規化。
- `appsettings.json` と `README.md` で設定・使い方を整備。

## Tests
- `dotnet build tools/MigrateDcbCosmosEventsTags/MigrateDcbCosmosEventsTags.csproj`

## Notes
- `--confirm` なしは JSONL 退避のみ（削除/再作成/アップロードは実行しない）。
- Orleans 停止前提で実行すること。
