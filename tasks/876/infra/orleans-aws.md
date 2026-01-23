# Issue 876: AWS向け Orleans インフラ設計案（永続ストリーム/SQS）

## 目的/前提
- DCBのイベントストアは DynamoDB、スナップショットは S3 を利用する（既存方針の継続）。
- Orleans は **公式パッケージ** を使用する（`Microsoft.Orleans.Streaming.SQS`）。
- 中小規模システム向けに、**費用が読みやすく安定** する構成を優先する。
- Orleans の **スケールアウト** を前提にするが、**API + Silo は同一プロセス**でまとめて水平拡張する。
- **外部公開は API の HTTP のみ**。Silo/Gateway は VPC 内部通信に限定する。

## 現状（DynamoDB アプリ）
- Orleans は `UseLocalhostClustering()` + Memory Streams/Memory Grain Storage。
- Orleans 側は永続化しないため、再起動時に Grain State/Stream は消える。

## 提案A（推奨）: RDS(AdoNet) + SQS Streams（公式パッケージ）
### コンセプト
Orleans のクラスタ管理／Grain State／Reminders／PubSubStore は **公式の AdoNet Provider** で RDS に集約。
ストリームは **SQS を使った永続ストリーム** とし、スケールアウトに対応する。

### 利用サービス（AWSマネージド）
- **RDS PostgreSQL (Single-AZ)**: Orleans クラスタリング／Grain State／Reminders／PubSubStore
- **SQS**: Orleans 永続ストリーム（`Microsoft.Orleans.Streaming.SQS`）
- **DynamoDB**: DCB イベントストア
- **S3**: スナップショット（大容量 State のオフロード）
- **ECS Fargate**: Orleans Silo + API（同一タスクで稼働）
- **ALB**: 外部公開（HTTPのみ）
- **CloudWatch Logs**: ログ集約
- **Secrets Manager / SSM**: 接続文字列管理

> App Runner は **複数ポート公開ができない**ため、
> Orleans の Silo/Gateway ポートが必要な構成では **ECS Fargate** を推奨する。

### Orleans 設定方針
- **クラスタリング**: `UseAdoNetClustering` (RDS)
- **Grain State**: `AddAdoNetGrainStorage` (RDS)
- **Reminders**: `UseAdoNetReminderService` (RDS)
- **Streams**: `AddSqsStreams` / `AddSqsStreamProvider`
- **PubSubStore**: RDS の Grain Storage を指定

### 初期パラメータ（小中規模向け・固定値）
> 各プロジェクトで後から調整する前提の「初期値」
- **SQS Queue Count**: 4（最大Silo数が少ない前提の固定値）
- **Visibility Timeout**: 60s（処理時間が伸びる場合は2倍に調整）
- **Message Retention**: 4 days
- **DLQ**: 有効化 / `maxReceiveCount=5`

### スケールアウト設計（バックエンド）
- **API + Silo を同一プロセス**で配置し、**単一サービスとして水平拡張**する
- スケール条件（初期）
  - CPU/メモリ + SQS の `ApproximateAgeOfOldestMessage` を併用

### ポート/ネットワーク設計
- Orleans には以下の内部ポートが必要
  - **Silo Port**: クラスタ通信
  - **Gateway Port**: Orleans Client 通信
- **外部公開は API の HTTP のみ**
- **Silo/Gateway は VPC 内部のみ許可**
  - ECS の Security Group で **同一SG内のみ受信**
  - ALB は **HTTPのみ**を許可
- API + Silo 同居のため、外部から Orleans Client で直接接続しない運用を前提

> デフォルトの Silo/Gateway ポートは `11111/30000`。
> 明示的に設定して、Security Group に反映する。

### コスト安定化のポイント
- RDS は **Single-AZ + 小さめインスタンス** で固定費を抑える
- SQS は従量課金で低負荷時の固定費が小さい
- Fargate は **最小タスク1** で安定運用

## 提案B（高スループット）: RDS + MSK (Kafka Streams)
### コンセプト
高スループット/長期スケールを重視する場合は **MSK** をストリーム基盤にする。

### 注意点
- 公式Kafka対応の制約/成熟度は別途検証が必要
- 運用コストが上がりやすく、中小規模には過剰になりやすい

## 推奨案まとめ
- **提案A（RDS + SQS Streams）を標準構成とする**
- Orleans は公式パッケージで成立
- AWS マネージド中心で、費用と運用バランスが良い
- API + Silo 同居で **単一サービスの水平拡張**にする

## 実装・移行のToDo
1) Orleans AdoNet 用の **RDS スキーマ作成**（Orleans 公式 SQL を利用）
2) Orleans の **Silo/Gateway ポート**を明示設定
3) `Microsoft.Orleans.Streaming.SQS` を導入し **SQS Streams** に切替
4) ECS Fargate の SG/ALB ルールに **内部ポートを追加**

## 追加の検討事項
- RDS Multi-AZ が必要ならコスト増を見込む
- SQS のキュー分割数は初期固定で、必要に応じて見直す
- 監視は CloudWatch + Orleans Metrics を統合
