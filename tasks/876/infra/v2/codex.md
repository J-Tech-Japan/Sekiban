# Issue 876: AWS Infra & Deploy Design (v2 / Codex)

## 目的（ユーザー要件の再確認）
- **Orleans 公式パッケージ**で構成する。
- **AWS マネージド**中心で運用負担を下げる。
- **小中規模のコスト安定性**を優先する。
- **スケールアウト**前提、**メモリーストリーム禁止**。
- **API + Silo は同一プロセスで水平拡張**（小中規模運用）。
- **外部公開は API HTTP のみ**、Silo/Gateway は内部通信。
- **ローカル設定/CLI/IaC/GitHub Actions**でデプロイ可能にする。

---

## 結論（謙虚なベスト案）
**最小固定費 + 公式パッケージ + AWSマネージド**のバランスから、
**Orleans も AWS DynamoDB/SQS で完結**させる構成を第一候補とする。

### 推奨構成（標準）
- **Orleans Clustering/Grain/Reminders**: DynamoDB (公式パッケージ)
- **Orleans Streams**: SQS (公式 `Microsoft.Orleans.Streaming.SQS`)
- **DCB Event Store**: DynamoDB
- **Snapshots**: S3
- **Compute**: ECS Fargate (API+Silo同居)
- **Ingress**: ALB (HTTPのみ)

#### なぜこの構成が“ベスト”か
- RDS を省くことで **固定費を下げ、運用も簡素**。
- Orleans の公式 AWS パッケージだけで完結。
- SQS は **従量課金で小規模に強い**。
- DCB 側とストレージを統一（DynamoDB + S3）。

> ただし、Orleans の Grain State が大規模になり始めると
> DynamoDB の Read/Write コストが増える可能性があるため、
> **中規模以上では RDS への切替を検討**できる構造にしておく。

---

## 代替構成（条件付き）
### 代替A: RDS(AdoNet) + SQS
- Grain State/Clustering/Reminders を RDS に集約
- **固定費は増えるがコストが読みやすい**
- **API 低トラフィックだがデータは重い**ケースに向く

> 初期は DynamoDB 構成、
> 予算や負荷次第で RDS に段階的に移行できる方針が安全。

---

## 公式パッケージ一覧（想定）
- `Microsoft.Orleans.Clustering.DynamoDB`
- `Microsoft.Orleans.Persistence.DynamoDB`
- `Microsoft.Orleans.Reminders.DynamoDB`
- `Microsoft.Orleans.Streaming.SQS`

---

## スケールアウト/ネットワーク設計（小中規模向け）
### デプロイ単位
- **API + Silo を同一タスク**に配置
- **ECS Fargate のタスク数で水平スケール**

### ポート設計
- **HTTP (API)**: 外部公開（ALB）
- **Silo Port**: 内部のみ
- **Gateway Port**: 内部のみ

> App Runner は複数ポート公開ができないため非推奨。
> ECS Fargate で **ALBはHTTPのみ公開**し、Silo/GatewayはSG内通信に限定。

### セキュリティグループ
- ALB → ECS: HTTP のみ
- ECS → ECS: Silo/Gateway (同一SG内のみ)

---

## SQS Streams 初期値（小中規模前提）
> 各プロジェクトで調整可能な初期固定値
- **Queue Count**: 4
- **Visibility Timeout**: 60s
- **Retention**: 4日
- **DLQ**: 有効、`maxReceiveCount=5`

---

## IaC 設計（Bicep相当の運用感）
### 方針
- **CDK** を推奨（C# or TypeScript）
  - .NET中心なら **C# CDK** が最も自然
  - 既存運用に合わせて TS でも可
- **パラメータ差分で設計を切替**できる構造

### ディレクトリ例
```
infra/
  cdk.json
  src/
    Stacks/
      NetworkStack.cs
      DataStack.cs
      MessagingStack.cs
      ComputeStack.cs
    Constructs/
      EcsFargateService.cs
      DynamoDbTables.cs
      SqsQueues.cs
  config/
    dev.json
    stg.json
    prod.json
  scripts/
    deploy.sh
```

---

## デプロイ運用（3パス）
### 1) ローカル設定
- `infra/config/{env}.json` で環境切替
- `AWS_PROFILE` / `AWS_REGION` でスイッチ

### 2) CLI + IaC
- `./scripts/deploy.sh dev` のように CLI でデプロイ可能
- CDK の `--context env=dev` で切替

### 3) GitHub Actions
- OIDC で AssumeRole
- `main` へのマージで `stg`、タグで `prod`
- CDK deploy をそのまま実行

---

## 運用メモ（謙虚な確認事項）
- **Orleans のIP広告**は ECS Task ENI を正しく使う必要がある
  - ECS メタデータから取得する実装が必要か要確認
- **ALBコスト**が最小構成でも固定費の大部分になる
  - 低コスト優先なら NLB/ALB どちらが良いか再検討

---

## まとめ
- **DynamoDB + SQS + S3 + ECS** が小中規模の“ベスト”構成。
- 公式パッケージで完結し、AWSマネージド中心で運用しやすい。
- **API+Silo同居で水平スケール**し、Silo/Gatewayは内部通信に限定。
- IaC は CDK で **Bicep相当のパラメータ切替運用**が可能。

