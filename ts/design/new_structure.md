# Sekiban Package Architecture Discussion Log

## セッション情報
- **セッションID**: b2d72e4b-c649-40db-b133-9fa916db16b0
- **モデル**: claude-opus-4-20250514
- **作業ディレクトリ**: `/Users/tomohisa/dev/GitHub/Sekiban-ts/ts/src/packages/postgres`
- **日時**: 2025-07-04 19:59-20:02 (JST)

---

## 会話内容

### Assistant からの回答 (20:00:15)

**質問**: パッケージアーキテクチャについて - Sekiban が npm に公開される際の、ドメインと API 開発者のプロジェクト構造と、ワークフローの実装場所について

**回答**: 

#### 推奨ユーザープロジェクト構造

```
my-app/
├── packages/
│   ├── domain/                    # 純粋なドメインロジック
│   │   ├── src/
│   │   │   ├── aggregates/
│   │   │   │   ├── user/
│   │   │   │   │   ├── events/
│   │   │   │   │   ├── commands/
│   │   │   │   │   ├── projectors/
│   │   │   │   │   └── queries/
│   │   │   │   └── order/
│   │   │   ├── projections/       # Multi-projections
│   │   │   ├── value-objects/
│   │   │   └── generated/         # Generated registry
│   │   ├── package.json
│   │   └── tsconfig.json
│   │
│   ├── workflows/                 # 独立したワークフローパッケージ（推奨）
│   │   ├── src/
│   │   │   ├── user-onboarding/
│   │   │   ├── order-fulfillment/
│   │   │   └── payment-processing/
│   │   ├── package.json
│   │   └── tsconfig.json
│   │
│   ├── api/                       # REST/GraphQL API
│   │   ├── src/
│   │   │   ├── routes/
│   │   │   └── server.ts
│   │   └── package.json
│   │
│   └── worker/                    # バックグラウンドジョブ/サガ
│       ├── src/
│       └── package.json
│
├── package.json                   # ワークスペースルート
└── tsconfig.json
```

#### ワークフローを分離する理由（推奨）

**✅ オプション1: ワークフローを独立パッケージにする**

利点:
- **明確な分離** - ドメインは純粋、ワークフローはアプリケーションロジック
- **独立したデプロイ** - ドメインに触れることなくワークフローを更新可能
- **異なるテスト戦略** - ワークフローは統合テスト、ドメインは単体テスト
- **チーム境界** - ドメインチーム vs アプリケーションチーム
- **再利用性** - 複数のワークフローパッケージが同じドメインを使用可能

**❌ オプション2: ワークフローをドメインパッケージに含める**

問題点:
- DDD原則に違反（ワークフローはアプリケーションサービス）
- ドメインパッケージが重くなる
- ドメインとワークフローのデプロイが結合される

#### Sekiban npm パッケージ構造

```
@sekiban/core           # コア抽象化、インターフェース
@sekiban/postgres       # PostgreSQL イベントストア
@sekiban/cosmos         # Cosmos DB イベントストア  
@sekiban/orleans        # Orleans grain 統合
@sekiban/dapr           # Dapr actor 統合
@sekiban/workflow       # ワークフロー/サガ抽象化
@sekiban/testing        # テストユーティリティ
@sekiban/codegen        # タイプレジストリのコード生成
```

#### ユーザーパッケージの依存関係

**domain/package.json**
```json
{
  "name": "@myapp/domain",
  "dependencies": {
    "@sekiban/core": "^1.0.0",
    "zod": "^3.0.0"
  },
  "devDependencies": {
    "@sekiban/codegen": "^1.0.0",
    "@sekiban/testing": "^1.0.0"
  }
}
```

**workflows/package.json**
```json
{
  "name": "@myapp/workflows",
  "dependencies": {
    "@sekiban/core": "^1.0.0",
    "@sekiban/workflow": "^1.0.0",
    "@myapp/domain": "workspace:*"
  }
}
```

**api/package.json**
```json
{
  "name": "@myapp/api",
  "dependencies": {
    "@sekiban/core": "^1.0.0",
    "@sekiban/postgres": "^1.0.0",
    "@myapp/domain": "workspace:*",
    "@myapp/workflows": "workspace:*",
    "express": "^4.0.0"
  }
}
```

#### ワークフロー実装の推奨事項

```typescript
// ユーザーのワークフロー実装例
export const orderFulfillmentWorkflow = new Workflow({
  name: 'OrderFulfillment',
  version: 1,
  context: z.object({
    orderId: z.string(),
    customerId: z.string()
  }),
  steps: [
    {
      name: 'ValidateInventory',
      command: (ctx) => new ValidateInventoryCommand(ctx.orderId)
    },
    {
      name: 'ChargePayment',
      command: (ctx) => new ChargePaymentCommand(ctx.orderId),
      compensation: (ctx) => new RefundPaymentCommand(ctx.orderId)
    },
    {
      name: 'ShipOrder',
      command: (ctx) => new ShipOrderCommand(ctx.orderId)
    }
  ]
});
```

#### プロジェクトテンプレートの推奨

```bash
npx create-sekiban-app my-app --template typescript
```

生成されるプロジェクト:
```
my-app/
├── packages/
│   ├── domain/
│   ├── workflows/
│   ├── api/
│   └── web/
├── docker-compose.yml
├── package.json
└── README.md
```

この構造により以下が実現される:
- 関心事の明確な分離
- コンポーネントの独立したデプロイの柔軟性
- 動作例による簡単なオンボーディング
- 組み込まれたベストプラクティス

---

### User からの要求 (20:02:16)

**内容**: 
推奨アプローチで進めることに同意。以下のファイルを更新して、Zodベースのタイプレジストリソリューションに移行したい:

1. `type-registry.md`
2. `/Users/tomohisa/dev/GitHub/Sekiban-ts/ts/design/type-registry-progress.md`

大きな変更が必要になる可能性があるため、twadaのTDDスタイルに従って、まず計画を書くよう要求。

---

## まとめ

この会話では、Sekiban フレームワークの npm パッケージ化に伴うプロジェクト構造について詳細に議論されました。主なポイント:

1. **ワークフローの分離**: ドメインパッケージとは独立したワークフローパッケージを推奨
2. **パッケージ構造**: `@sekiban/*` の複数パッケージでの提供
3. **ユーザープロジェクト**: モノレポ構造での domain/workflows/api の分離
4. **次のステップ**: Zodベースのタイプレジストリへの移行計画の策定