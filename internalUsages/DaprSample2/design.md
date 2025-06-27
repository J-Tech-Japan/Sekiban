# Daprスケジューラ接続エラーの解決策

## 1. 問題の概要

`dapr run` を使用してアプリケーションを起動した際に、以下のエラーが継続的に発生し、Actorベースの操作が失敗する。

```log
ERRO[... ] Error connecting to Schedulers, reconnecting: failed to connect to scheduler host: failed to watch scheduler hosts: rpc error: code = Unavailable desc = connection error: desc = "transport: Error while dialing: dial tcp [::1]:50006: connect: connection refused"
```

このエラーは、DaprサイドカーがDaprのPlacement Service（スケジューラ）に接続できていないことを示している。

## 2. 原因分析

エラーメッセージ `connection refused` は、サイドカーが接続を試みている `localhost:50006` でリッスンしているプロセスが存在しないことを意味する。これは、ローカル開発環境においてDaprのコントロールプレーンサービスが正しく起動していない、または正常に機能していない場合に発生する典型的な問題である。

主な原因として、以下の可能性が考えられる。

*   **Daprが初期化されていない、または正常に完了していない**: `dapr init` が実行されていないか、途中で失敗している。
*   **Docker Desktopが起動していない**: DaprのコントロールプレーンはDockerコンテナとして実行されるため、Dockerが必須である。
*   **既存のDaprプロセスが異常な状態**: 以前のDaprプロセスが正しく終了せず、環境が不安定になっている。

## 3. 解決手順

以下の手順でローカルのDapr環境をリセットし、クリーンな状態で再起動することで、問題を解決する。

### Step 1: Daprの稼働状況を確認する

まず、現在のDaprコントロールプレーンの状態を確認する。

```bash
dapr status
```

このコマンドの実行結果で、`dapr-placement` サービスが `Running` 状態でない場合、コントロールプレーンが正常に動作していないことが確定する。

### Step 2: Dapr環境をリセットする

Dapr環境を完全にクリーンな状態に戻すため、アンインストールと再初期化を行う。

**注意:** この操作を実行する前に、**Docker Desktopが起動していること**を確認してください。

```bash
# 既存のDaprコントロールプレーン（コンテナ）を削除します
dapr uninstall

# Daprを再初期化します
dapr init
```

`dapr init` が成功すると、`dapr-placement`, `dapr-sentry`, `dapr-redis` などのコントロールプレーンコンテナがDocker上に作成され、起動する。

### Step 3: アプリケーションを再実行する

Dapr環境が正常にリセットされた後、再度アプリケーションを起動する。

```bash
# internalUsages/DaprSample2 ディレクトリにいることを確認
cd internalUsages/DaprSample2

# dapr run コマンドでアプリケーションを起動
dapr run \
  --app-id counter-demo \
  --app-port 5003 \
  --dapr-http-port 3501 \
  --dapr-grpc-port 50002 \
  --resources-path ./dapr-components \
  -- dotnet run --urls "http://localhost:5003"
```

この手順により、Daprサイドカーは正常にPlacementサービスに接続できるようになり、アプリケーションは正しく動作するはずである。
