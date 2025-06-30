# Dapr スケジューラ接続問題の調査と解決方法 🔍

## 問題の概要

`DaprSample2` プロジェクトにおいて、`dapr run` コマンドでアプリケーションを実行する際に、以下のエラーが継続的に発生している：

```log
ERRO[... ] Error connecting to Schedulers, reconnecting: failed to connect to scheduler host: failed to watch scheduler hosts: rpc error: code = Unavailable desc = connection error: desc = "transport: Error while dialing: dial tcp [::1]:50006: connect: connection refused"  app_id=counter-demo instance=Mac scope=dapr.runtime.scheduler type=log ver=1.15.5
```

## 技術分析

### 1. エラーの根本原因

このエラーはDapr sidecarがDapr Schedulerサービス（ポート50006）に接続できないことを示している。ポート50006は以下のDaprコンポーネントで使用される：

- **Dapr Scheduler Service**: Jobs、Workflows、Actor Remindersなどの機能を提供
- **デフォルトアドレス**: Kubernetesでは `dapr-scheduler-server:50006`

### 2. Placement Service vs Scheduler Service

Dapr 1.14以降では、以下の2つの重要なサービスが存在する：

#### Placement Service (ポート50005)
- Actor配置とルーティングを管理
- Actor間通信のために必要
- アドレス: `dapr-placement-server:50005`

#### Scheduler Service (ポート50006)  
- Workflows、Jobs、Actor Remindersを管理
- Dapr 1.14で導入された新しいサービス
- アドレス: `dapr-scheduler-server:50006`

### 3. ローカル開発環境での問題

ローカル環境（Standalone Mode）では、以下が必要：
- Placement Service: `localhost:50005`  
- Scheduler Service: `localhost:50006`

`dapr init` を実行すると、通常これらのサービスがDockerコンテナとして起動されるが、何らかの理由でScheduler Serviceが起動していない可能性がある。

## Webリサーチからの解決策

### 1. Dapr公式ドキュメントの推奨解決法

#### 基本的なトラブルシューティング手順
```bash
# Daprの現在の状態を確認
dapr status

# Daprを完全にアンインストール
dapr uninstall

# Docker環境の確認（必要に応じて）
docker ps -a

# Daprの再初期化
dapr init
```

#### Dapr Statusの確認
正常な状態では以下のサービスが `Running` 状態である必要がある：
- `dapr-placement`（ポート50005）
- `dapr-scheduler`（ポート50006） 
- `dapr-sidecar-injector`
- `dapr-sentry`

### 2. Docker関連の確認事項

macOSでは以下の設定を確認：
- Docker Desktopが起動していること
- Docker Desktopの設定で "Allow the default Docker socket to be used" が有効になっていること

### 3. ポート競合の確認

```bash
# ポート50006の使用状況を確認
lsof -i :50006

# ポート50005の使用状況を確認  
lsof -i :50005
```

### 4. 環境変数による回避策

特定の環境でscheduler接続を無効化する場合：
```bash
# Scheduler接続を無効化（Actor Remindersなどが使用できなくなる）
export DAPR_SCHEDULER_HOST_ADDRESS=" "

# または明示的にアドレスを指定
export DAPR_SCHEDULER_HOST_ADDRESS="127.0.0.1:50006"
```

### 5. Dapr CLI の引数による設定

`dapr run` コマンドで明示的にSchedulerアドレスを指定：
```bash
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address "127.0.0.1:50006" \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

## GitHub Issues からの追加情報

### Dapr 1.14.4での既知の問題

GitHubの調査により、Dapr 1.14.4でScheduler接続に関する既知の問題があることが判明：

1. **Issue #8214**: "Connection Error: Failed to Watch Scheduler Jobs in Dapr 1.14.4"
2. **Issue #8100**: "Allow user to completely remove/disable scheduler in 1.14+"

### 解決策オプション

#### オプション1: Schedulerを無効化
Actor Reminders、Workflows、Jobsを使用しない場合：
```bash
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address " " \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

#### オプション2: Daprのバージョンダウングレード
Dapr 1.13.x系への一時的なダウングレード：
```bash
# 現在のバージョンをアンインストール
dapr uninstall

# 特定バージョンのインストール
dapr init --runtime-version 1.13.5
```

#### オプション3: Microsoft Content Filterの無効化（macOS）
macOSでmDNSトラフィックがブロックされている場合：
```bash
# Microsoft Content Filterを無効化
mdatp system-extension network-filter disable
```

## 推奨解決手順

### ステップ1: 現状確認
```bash
# Daprの状態確認
dapr status

# Dockerコンテナの確認
docker ps -a | grep dapr
```

### ステップ2: 完全リセット
```bash
# Daprの完全アンインストール
dapr uninstall

# Dockerコンテナのクリーンアップ（必要に応じて）
docker system prune -f

# Daprの再初期化
dapr init
```

### ステップ3: 再実行
```bash
cd internalUsages/DaprSample2

# 基本実行
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

### ステップ4: 代替案（Scheduler無効化）
基本実行が失敗する場合：
```bash
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --scheduler-host-address " " \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

## 影響範囲

### Scheduler無効化の影響
- ✅ Actor基本機能（状態管理、メソッド呼び出し）は正常に動作
- ❌ Actor Remindersが使用できない
- ❌ Workflows機能が使用できない  
- ❌ Jobs機能が使用できない

### 現在のプロジェクトへの影響
`DaprSample2`では基本的なActor機能（CounterActor）のみを使用しているため、Scheduler無効化による機能的な影響はない。

## 追加の検証項目

1. **ファイアウォールの確認**: macOSのファイアウォール設定
2. **ネットワーク設定**: VPN接続やプロキシ設定の影響
3. **Docker Desktop設定**: リソース制限や設定の確認
4. **Daprログの詳細確認**: `--log-level debug` での実行

## 結論

この問題は主にDapr 1.14以降でのScheduler Service導入に起因する環境構築の問題である。基本的なActor機能の検証であれば、Schedulerを無効化することで問題を回避できる。本格的な運用環境では、適切なDapr環境の構築が必要となる。
