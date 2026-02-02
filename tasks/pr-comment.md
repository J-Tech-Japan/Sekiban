## Summary
- ServiceId によるテナント分離を DCB コア〜各ストレージ〜Orleans まで一貫対応。
- WASM/primitive projection を見据えた contract/actor を追加。
- CosmosDB legacy events/tags を新形式(/pk)へ移行する一括コンバージョンツールを追加。

## Changes
- ServiceId のコア抽象/実装を追加し、イベント/タグ/プロジェクション状態の分離基盤を整備。
- Cosmos/Postgres/DynamoDB/SQLite/InMemory に ServiceId 対応を反映。
- Orleans のストリーム/Grain を ServiceId でスコープし、TagState/TagConsistency も分離。
- ServiceId 分離のテストを追加（InMemory/Orleans）。
- primitive projection contract/actor を追加。
- SonarCloud 指摘対応（定数化、複雑度分割、SQL識別子の安全化）。
- `tools/MigrateDcbCosmosEventsTags` を新規追加（export/delete/recreate/import を一括実行）。
  - 変換ロジックで `serviceId`/`pk`/`timestamp`/`createdAt` の欠落を補完し、payload/tags を正規化。
  - `appsettings.json` と `README.md` で設定・使い方を整備。
  - 生成される `cosmos-backup/` を `.gitignore` に追加。

## Tests
- `dotnet build tools/MigrateDcbCosmosEventsTags/MigrateDcbCosmosEventsTags.csproj`

## Notes
- `--confirm` なしは JSONL 退避のみ（削除/再作成/アップロードは実行しない）。
- Orleans 停止前提で実行すること。
