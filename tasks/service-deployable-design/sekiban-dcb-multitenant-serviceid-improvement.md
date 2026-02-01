# 改善案（推奨）: ServiceId マルチテナント設計

## 目的
既存設計のリスク（PK衝突、legacy混在、ルーティング不明確、Postgres制約漏れ、defaultへの誤書き込み）を最小化しつつ、現在の情報だけで最も実装可能性が高い推奨構成を示す。

## 推奨方針（結論）
1) **ServiceId を厳格に正規化・検証**し、PK構成子として安全に利用できる形式に限定する。
2) **Cosmos は新規テナントを常に /pk コンテナへ**、legacy は **`default` だけ**に限定し、明示的ルーティングで混在を排除する。
3) **Cosmos の SQL は必ずパラメータ化**して ServiceId を埋め込みで使わない。
4) **Postgres のユニーク制約・PKに service_id を含める**（必要箇所のみ）ことでテナント衝突を防ぐ。
5) **非HTTPコンテキストでの default への暗黙書き込みを禁止**し、明示 ServiceId を必須化する。

この組み合わせが、運用事故とデータリークの確率を最小化しつつ、段階移行を可能にする現実的な最短ルート。

---

## 1. ServiceId 仕様（必須）
**推奨ルール**
- 文字種: `a-z`, `0-9`, `-` のみ（小文字化）
- 長さ: 1〜64
- 禁止文字: `|`, `/`, 空白, 制御文字
- 形式: `^[a-z0-9-]{1,64}$`

**理由**
- Cosmos の複合 PK（`{serviceId}|{key}`）で区切り衝突を回避。
- SQL/ログ/診断で扱いやすく、正規化が容易。

**実装指針**
- `IServiceIdProvider` 実装内で検証し、違反は例外。
- `DefaultServiceIdProvider.DefaultServiceId = "default"` を唯一の既定値として固定。

---

## 2. Cosmos DB の推奨運用
### 2.1 ルーティング規則（明文化）
- **`serviceId == "default"` のみ legacy を許可**。
- それ以外の ServiceId は **必ず /pk コンテナ**。

**具体的手段**
- `UseLegacyPartitionKeyPaths = true` は **単一テナント専用**と明記。
- マルチテナント環境では **container name override** により `events_v2` 等へ切替。

### 2.2 ルーティングの実装方針
- `ICosmosContainerResolver`（または options の `Func<string, ContainerNames>`）を導入し、
  ServiceId からコンテナ名を決定。
- `default` は legacy container、それ以外は v2 container へ。

### 2.3 SQL パラメータ化
- `QueryDefinition` に `@serviceId`, `@since` 等を渡す。
- 文字列補間は完全禁止。

**理由**
- 文字列補間は ServiceId に特殊文字が混入した場合の障害要因。

---

## 3. Postgres の推奨運用
### 3.1 制約の見直し
- 既存 UNIQUE/PK がテナント境界を跨ぐ場合は `(service_id, ...)` へ拡張。

例:
- `events(id)` が一意なら `PRIMARY KEY (service_id, id)` を検討。
- `tags(tag, sortable_unique_id)` が一意なら `(service_id, tag, sortable_unique_id)` へ。

### 3.2 RLS（推奨: 任意で導入）
- SaaS で運用リスクが高い場合は RLS を有効化。
- ただし導入コストと監視（`current_setting` の設定漏れ）に注意。

---

## 4. DI・実行時の安全策
### 4.1 default への暗黙書き込み禁止
- `AddSekibanDcbCosmosDbFull` の fallback を `DefaultServiceIdProvider` ではなく
  **例外を投げる Provider** に変更することを推奨。

例:
```csharp
public sealed class MissingServiceIdProvider : IServiceIdProvider
{
    public string GetCurrentServiceId() => throw new InvalidOperationException(
        "ServiceId is required in non-HTTP context");
}
```

### 4.2 明示 ServiceId の注入
- バックグラウンド処理や Orleans 側は `FixedServiceIdProvider` を強制。

---

## 5. 推奨移行パターン（現状情報で最適）
### Cosmos
- **新規 SaaS は v2 コンテナから開始**（`/pk`）。
- 既存 `default` のみ legacy 維持。
- 将来的に `default` を v2 へ移行する場合は、段階的バックフィルを行う。

### Postgres
- 既存データは `service_id='default'` で維持。
- 制約変更（PK/UNIQUE拡張）を先行し、アプリ変更を後追い。

---

## 6. 追加テスト（最低限）
- ServiceId バリデーションの単体テスト。
- `default` と `tenant-xxx` のデータが混線しないことの結合テスト。
- ルーティング（legacy/v2）判定のテスト。

---

## まとめ
- **ServiceId 仕様の明確化＋legacyを default 限定**が最も重要。
- Cosmos は **v2コンテナ固定＋SQLパラメータ化**で安全性を確保。
- Postgres は **service_id を含む制約**でテナント衝突を防止。
- 非HTTP時の default フォールバック禁止で事故を抑止。

この改善案は、現在提示済み情報を前提に実装コストとリスク低減のバランスが最良の構成。
