# Issue 876: AWS IaC/デプロイ設計（Bicep相当の運用フロー）

## 目的
- **ローカルから設定可能**（プロジェクトごとの設定を切替）
- **CLI + IaC でデプロイ可能**
- **GitHub Actions で継続デプロイ可能**
- Azure の Bicep 構成と同様に、**設計差分をパラメータで切り替えられる** IaC を目指す

## 推奨IaC方式
### 方式A（推奨）: **AWS CDK (TypeScript)**
- Bicep と同様に **コード化された IaC**
- CloudFormation に変換され、AWS公式スタックとして運用可能
- 設計差分を **コンフィグ/Context** で切替可能
- CLI（`cdk deploy`）と GitHub Actions の両方に適合

### 方式B（代替）: Terraform
- マルチクラウド/複雑な差分管理に強い
- チーム運用/標準化で採用済みならこちら

> 本設計では **CDK 前提**で記載

---

## IaC ディレクトリ設計（例）
```
infra/
  cdk.json
  package.json
  tsconfig.json
  bin/
    app.ts               # エントリポイント
  stacks/
    base-network.ts      # VPC / Subnet / SG
    data-layer.ts        # RDS / DynamoDB / S3
    messaging.ts         # SQS
    compute.ts           # ECS Fargate / ALB
    observability.ts     # CloudWatch / X-Ray
  modules/
    ecs-service.ts       # API+Silo 同居サービス定義
    rds.ts
    sqs.ts
  config/
    dev.json
    stg.json
    prod.json
  scripts/
    deploy.sh            # CLI実行用（環境切替）
    synth.sh
```

### config例（dev.json）
```json
{
  "envName": "dev",
  "region": "ap-northeast-1",
  "vpcCidr": "10.0.0.0/16",
  "ecs": {
    "cpu": 512,
    "memory": 1024,
    "desiredCount": 1,
    "maxCount": 2
  },
  "rds": {
    "instanceClass": "t4g.small",
    "multiAz": false
  },
  "sqs": {
    "queueCount": 4,
    "visibilityTimeoutSec": 60,
    "retentionDays": 4
  }
}
```

---

## 1) ローカルから設定可能
- `infra/config/{env}.json` で環境別設定を切替
- `AWS_PROFILE` / `AWS_REGION` を CLI で指定可能
- secrets は **SSM/Secrets Manager** を利用し、ローカルは最小限のセットのみ

### ローカル実行例
```bash
cd infra
AWS_PROFILE=dev aws sso login
npm install
npm run deploy -- --context env=dev
```

---

## 2) CLI + IaC でデプロイ可能
- `scripts/deploy.sh` で env 切替
- CDK の `--context env=dev` で config 読み込み

```bash
./scripts/deploy.sh dev
./scripts/deploy.sh prod
```

---

## 3) GitHub Actions で継続デプロイ
### 方針
- **OIDC で AWS に AssumeRole**（長期キー不要）
- `main` へのマージで `stg`、タグ付けで `prod` などに切替

### GitHub Actions 概要
- `aws-actions/configure-aws-credentials` で AssumeRole
- `npm ci` + `cdk deploy` を実行
- `config/{env}.json` を利用して環境別デプロイ

---

## IaCで定義する主要リソース
- **VPC / Subnet / SG**
- **ECS Fargate Service**（API+Silo 同居）
- **ALB**（HTTPのみ公開）
- **RDS PostgreSQL**（Orleansクラスタ/Grain/Reminder）
- **SQS**（Orleans Streams）
- **DynamoDB**（DCB Event Store）
- **S3**（スナップショット）
- **CloudWatch Logs**

---

## 運用モデル（Bicepと同等の運用感）
- `config/*.json` で **設計差分を吸収**
- `cdk deploy` で同一コードから複数環境展開
- GitHub Actions は **環境ごとに Role を分ける**

---

## まとめ
- Bicep と同様に **IaC + パラメータ切替**ができる設計
- ローカル/CLI/CI すべてに対応可能
- 中小規模向けに **API+Silo 同居** を前提にした最小構成
