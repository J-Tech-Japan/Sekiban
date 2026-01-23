# AWS Infrastructure Design v2 - Sekiban DCB + Orleans

> **v2 の目的**: 既存設計 (orleans-aws.md, claude.v1.md, iac-aws.md, iac-aws-claude.md) を統合し、矛盾点を整理した上でベストな設計を提案する。

---

## 既存設計のレビュー

### 共通点（確定事項）
| 項目 | 決定内容 |
|------|----------|
| コンピュート | ECS Fargate |
| ストリーム | SQS (`Microsoft.Orleans.Streaming.SQS`) |
| イベントストア | DynamoDB (DCB) |
| スナップショット | S3 |
| 外部公開 | ALB (HTTP/HTTPS のみ) |
| 内部通信 | Silo Port (11111), Gateway Port (30000) - VPC 内部のみ |
| IaC | AWS CDK |
| CI/CD | GitHub Actions + OIDC |

### 議論点（選択が必要）

| 項目 | Option A | Option B | 考察 |
|------|----------|----------|------|
| Orleans クラスタ/State | DynamoDB | RDS PostgreSQL | 後述 |
| CDK 言語 | C# | TypeScript | 後述 |
| 初期構成 | 2 silo | 1 silo | コスト vs 可用性 |

---

## 議論点の整理

### 1. Orleans データストア: DynamoDB vs RDS

#### DynamoDB (claude.v1.md の推奨)
```
✅ 長所
- 完全サーバーレス、従量課金
- Free Tier が永続 (25 RCU/WCU)
- DCB イベントストアと同じ技術スタック
- 運用が簡素

❌ 短所
- Orleans DynamoDB Provider は AdoNet より新しい
- Reminders Provider の成熟度に懸念あり
- トランザクション機能が限定的
```

#### RDS PostgreSQL (orleans-aws.md の推奨)
```
✅ 長所
- AdoNet Provider は Orleans で最も成熟
- トランザクション対応
- 既存の運用ノウハウが豊富
- Reminders/PubSubStore が安定

❌ 短所
- 固定費 (~$12/月、Free Tier 後)
- 別のデータベース技術が増える
- 運用コストが若干増加
```

#### 結論: **RDS PostgreSQL を推奨**

**理由:**
1. Orleans の AdoNet Provider は最も実績がある
2. 中小規模で月 $12 の固定費は許容範囲
3. 本番運用での安定性を優先すべき
4. DynamoDB は将来的な選択肢として残す

> **謙虚な補足**: DynamoDB Provider も公式パッケージであり、十分に動作する可能性が高い。チームが DynamoDB 運用に慣れている場合は Option B として採用可能。

---

### 2. CDK 言語: C# vs TypeScript

#### C# (iac-aws-claude.md の推奨)
```
✅ 長所
- アプリケーションと同じ言語
- .NET 開発者に馴染みやすい
- 型安全、IDE サポート良好

❌ 短所
- CDK コミュニティは TypeScript が主流
- サンプルコードが少なめ
```

#### TypeScript (iac-aws.md の推奨)
```
✅ 長所
- CDK のデファクト言語
- サンプル/ドキュメントが豊富
- 既存の CDK 資産が多い

❌ 短所
- .NET プロジェクトに別言語が入る
- TypeScript の知識が必要
```

#### 結論: **TypeScript を推奨**

**理由:**
1. CDK の公式サンプル/ドキュメントは TypeScript が圧倒的に多い
2. IaC は「一度書いたら触らない」ことが多く、言語統一の恩恵が薄い
3. トラブル時の情報収集が容易
4. Azure Bicep も YAML/独自 DSL であり、言語分離は許容されている

> **謙虚な補足**: チームが C# に強いこだわりがある場合は C# でも問題ない。AWS CDK は両方をサポートしている。

---

## 推奨アーキテクチャ

### 構成図
```
                 Internet
                    │
                    ▼
              ┌──────────┐
              │   ALB    │ ← HTTPS (443)
              └────┬─────┘
                   │ Port 8080 only
                   ▼
    ┌──────────────────────────────────────────────────┐
    │                  VPC (Private Subnet)            │
    │                                                  │
    │   ┌─────────────────┐    ┌─────────────────┐    │
    │   │   ECS Task #1   │    │   ECS Task #2   │    │
    │   │  ┌───────────┐  │    │  ┌───────────┐  │    │
    │   │  │  Orleans  │  │    │  │  Orleans  │  │    │
    │   │  │  Silo+API │  │    │  │  Silo+API │  │    │
    │   │  └───────────┘  │    │  └───────────┘  │    │
    │   │   :8080 (http)  │    │   :8080 (http)  │    │
    │   │   :11111 (silo) │◄──►│   :11111 (silo) │    │
    │   │   :30000 (gw)   │◄──►│   :30000 (gw)   │    │
    │   └─────────────────┘    └─────────────────┘    │
    │           │                      │              │
    │           └──────────┬───────────┘              │
    │                      ▼                          │
    │   ┌──────┐  ┌───────┐  ┌─────┐  ┌──────┐      │
    │   │ RDS  │  │DynamoDB│  │ SQS │  │  S3  │      │
    │   │Postgres│ │(Events)│  │     │  │(Snap)│      │
    │   │(Orleans)│ │        │  │     │  │      │      │
    │   └──────┘  └───────┘  └─────┘  └──────┘      │
    │                                                  │
    └──────────────────────────────────────────────────┘
```

### コンポーネント一覧

| レイヤー | サービス | 用途 | 月額コスト (概算) |
|----------|----------|------|-------------------|
| Compute | ECS Fargate (0.5vCPU/1GB) x 2 | Orleans Silo + API | ~$28-40 |
| Orleans Cluster/State | RDS PostgreSQL (db.t4g.micro) | Membership, Grain State, Reminders | ~$12 |
| Orleans Streams | SQS Standard | Persistent Streams | ~$0-2 |
| Event Store | DynamoDB On-Demand | DCB Events | ~$0-5 |
| Snapshots | S3 Standard | Large State Offload | ~$0-2 |
| Load Balancer | ALB | External Access | ~$18-24 |
| Service Discovery | Cloud Map | Silo Discovery | ~$0 |
| Secrets | Secrets Manager | Connection Strings | ~$1 |
| Logs | CloudWatch Logs | Monitoring | ~$1-3 |
| **合計** | | | **~$60-88/月** |

### Azure との比較

| 構成 | 月額コスト |
|------|-----------|
| Azure Container Apps (2 replicas) | ¥15,000-30,000 |
| Azure App Service B1 x 2 | ¥15,000~ |
| **AWS ECS Fargate (2 tasks)** | **¥9,000-13,000** |

> AWS の方がやや安価だが、ALB の固定費 (~$18) が大きい。低トラフィック時は Azure App Service B1 の方が安い可能性もある。

---

## Orleans 設定

### NuGet パッケージ (すべて公式)
```xml
<PackageReference Include="Microsoft.Orleans.Clustering.AdoNet" Version="9.*" />
<PackageReference Include="Microsoft.Orleans.Persistence.AdoNet" Version="9.*" />
<PackageReference Include="Microsoft.Orleans.Reminders.AdoNet" Version="9.*" />
<PackageReference Include="Microsoft.Orleans.Streaming.SQS" Version="9.*" />
<PackageReference Include="Npgsql" Version="8.*" />
```

### Silo 設定
```csharp
siloBuilder
    // Clustering - RDS PostgreSQL
    .UseAdoNetClustering(options => {
        options.ConnectionString = rdsConnectionString;
        options.Invariant = "Npgsql";
    })
    // Grain State - RDS PostgreSQL
    .AddAdoNetGrainStorage("OrleansStorage", options => {
        options.ConnectionString = rdsConnectionString;
        options.Invariant = "Npgsql";
    })
    // Reminders - RDS PostgreSQL
    .UseAdoNetReminderService(options => {
        options.ConnectionString = rdsConnectionString;
        options.Invariant = "Npgsql";
    })
    // Streams - SQS
    .AddSqsStreams("Default", options => {
        options.Service = awsRegion;
    })
    // Endpoint Configuration
    .Configure<EndpointOptions>(options => {
        options.SiloPort = 11111;
        options.GatewayPort = 30000;
        options.AdvertisedIPAddress = GetPrivateIpAddress();
    });
```

---

## ネットワーク設計

### Security Group ルール

```hcl
# ALB Security Group
resource "aws_security_group" "alb" {
  ingress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

# ECS Task Security Group
resource "aws_security_group" "ecs" {
  # ALB → ECS (HTTP API)
  ingress {
    from_port       = 8080
    to_port         = 8080
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  # ECS ↔ ECS (Orleans Silo)
  ingress {
    from_port = 11111
    to_port   = 11111
    protocol  = "tcp"
    self      = true
  }

  # ECS ↔ ECS (Orleans Gateway)
  ingress {
    from_port = 30000
    to_port   = 30000
    protocol  = "tcp"
    self      = true
  }

  # ECS → RDS
  egress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.rds.id]
  }
}
```

---

## IaC 設計 (AWS CDK TypeScript)

### ディレクトリ構成
```
infra/
├── cdk.json
├── package.json
├── tsconfig.json
├── bin/
│   └── app.ts                 # エントリポイント
├── lib/
│   ├── sekiban-stack.ts       # メインスタック
│   └── constructs/
│       ├── vpc.ts
│       ├── rds.ts
│       ├── dynamodb.ts
│       ├── sqs.ts
│       ├── s3.ts
│       ├── ecs.ts
│       └── alb.ts
├── config/
│   ├── dev.json
│   ├── stg.json
│   └── prod.json
├── scripts/
│   ├── deploy.sh
│   ├── build-push.sh
│   └── destroy.sh
└── .local/
    └── dev.json               # gitignore 対象
```

### config/dev.json
```json
{
  "environment": "dev",
  "region": "ap-northeast-1",

  "vpc": {
    "cidr": "10.0.0.0/16",
    "maxAzs": 2
  },

  "rds": {
    "instanceClass": "db.t4g.micro",
    "multiAz": false,
    "allocatedStorage": 20
  },

  "ecs": {
    "cpu": 512,
    "memory": 1024,
    "desiredCount": 2,
    "minCount": 1,
    "maxCount": 4
  },

  "orleans": {
    "clusterId": "sekiban-dev",
    "serviceId": "sekiban-service",
    "siloPort": 11111,
    "gatewayPort": 30000,
    "httpPort": 8080
  },

  "sqs": {
    "queueCount": 4,
    "visibilityTimeoutSec": 60,
    "messageRetentionDays": 4
  }
}
```

### デプロイフロー

```bash
# 1. ローカルからのデプロイ
cd infra
npm install
npm run deploy -- --context env=dev

# 内部で実行される処理:
# - cdk synth (CloudFormation 生成)
# - cdk deploy (インフラデプロイ)
# - docker build & push to ECR
# - ecs update-service (コンテナ更新)
```

### GitHub Actions
```yaml
name: Deploy

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    permissions:
      id-token: write
      contents: read

    steps:
      - uses: actions/checkout@v4

      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}
          aws-region: ap-northeast-1

      - uses: actions/setup-node@v4
        with:
          node-version: '20'

      - name: Deploy Infrastructure
        working-directory: infra
        run: |
          npm ci
          npx cdk deploy --require-approval never -c env=prod

      - name: Build and Push Image
        run: ./infra/scripts/build-push.sh prod

      - name: Update ECS Service
        run: |
          aws ecs update-service \
            --cluster sekiban-prod \
            --service orleans-silo \
            --force-new-deployment
```

---

## 実装優先順位

### Phase 1: 基盤 (Week 1-2)
1. [ ] RDS PostgreSQL に Orleans スキーマ作成
2. [ ] Orleans AdoNet Provider 統合テスト (ローカル Docker)
3. [ ] SQS Streams 統合テスト (LocalStack)

### Phase 2: IaC (Week 3-4)
4. [ ] CDK プロジェクト初期化
5. [ ] VPC / RDS / DynamoDB / SQS / S3 Construct 実装
6. [ ] ECS Fargate Construct 実装 (3 ports)
7. [ ] ALB / Security Group 実装

### Phase 3: CI/CD (Week 5)
8. [ ] GitHub Actions OIDC 設定
9. [ ] deploy.sh / build-push.sh 実装
10. [ ] Dev 環境デプロイ & 動作確認

### Phase 4: 本番準備 (Week 6)
11. [ ] RDS Multi-AZ 検討
12. [ ] Auto Scaling 設定
13. [ ] CloudWatch アラーム設定
14. [ ] Prod 環境デプロイ

---

## 代替案: DynamoDB オール構成

RDS を使わず DynamoDB で Orleans も構成する場合:

```csharp
siloBuilder
    .UseDynamoDBClustering(options => {
        options.Service = region;
        options.TableName = "OrleansCluster";
    })
    .AddDynamoDBGrainStorage("OrleansStorage", options => {
        options.Service = region;
        options.TableName = "OrleansGrainState";
    })
    // Reminders は DynamoDB Provider の成熟度を確認してから
    .AddSqsStreams("Default", options => {
        options.Service = region;
    });
```

**採用条件:**
- RDS の固定費を完全に避けたい場合
- DynamoDB 運用に習熟している場合
- Orleans DynamoDB Provider の検証が完了した場合

---

## まとめ

| 項目 | 決定 | 理由 |
|------|------|------|
| Orleans データストア | RDS PostgreSQL | AdoNet Provider の成熟度 |
| Orleans ストリーム | SQS | 公式パッケージ、従量課金 |
| コンピュート | ECS Fargate | 複数ポート対応、スケールアウト |
| IaC | CDK (TypeScript) | コミュニティ/ドキュメントの豊富さ |
| CI/CD | GitHub Actions + OIDC | セキュア、Azure と同様の運用感 |

**想定月額コスト: ~$60-88 (¥9,000-13,000)**

---

## 参考資料

- [Orleans AdoNet Configuration](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/relational-storage)
- [Microsoft.Orleans.Streaming.SQS](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS)
- [AWS CDK Developer Guide](https://docs.aws.amazon.com/cdk/v2/guide/home.html)
- [Running Orleans on AWS ECS](https://medium.com/@Sas_Amir/running-ms-orleans-cluster-in-aws-ecs-88edaaf4564b)
