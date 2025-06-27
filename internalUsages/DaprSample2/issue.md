# Daprサイドカーがスケジューラに接続できず、アプリケーションが起動しない問題 😥

## 概要

`dapr run` コマンドを使用して `DaprSample2` アプリケーションを起動しようとすると、Daprサイドカーがスケジューラサービスへの接続に失敗します。これにより、ターミナルにエラーログが繰り返し表示され、`CreateUser` のようなActorベースの操作が正常に実行できません。

## 問題を再現する手順

1.  プロジェクトのルートディレクトリにいることを確認します。
2.  `internalUsages/DaprSample2` ディレクトリに移動します。
    ```bash
    cd internalUsages/DaprSample2
    ```
3.  次の `dapr run` コマンドを実行して、アプリケーションとDaprサイドカーを起動します。
    ```bash
    dapr run \
      --app-id counter-demo \
      --app-port 5003 \
      --dapr-http-port 3501 \
      --dapr-grpc-port 50002 \
      --resources-path ./dapr-components \
      -- dotnet run --urls "http://localhost:5003"
    ```

## 観測された現象

コマンド実行後、ターミナルに以下のエラーが継続的に出力されます。

```log
ERRO[... ] Error connecting to Schedulers, reconnecting: failed to connect to scheduler host: failed to watch scheduler hosts: rpc error: code = Unavailable desc = connection error: desc = "transport: Error while dialing: dial tcp [::1]:50006: connect: connection refused"  app_id=counter-demo instance=Mac scope=dapr.runtime.scheduler type=log ver=1.15.5
```

このエラーは、アプリケーションのサイドカーがDaprのスケジューラ（Placement Service）と通信できていないことを示しています。

## 分析と仮説

このエラーメッセージ `connection refused` は、サイドカーが接続しようとしている `localhost:50006` でリッスンしているプロセスが存在しないことを強く示唆しています。

**主な原因の仮説:**

ローカルマシン上でDaprのコントロールプレーン（特に `dapr-placement` サービス）が正しく実行されていない可能性が非常に高いです。これは、以下のような状況で発生することがあります。

*   `dapr init` が正常に完了していない、または実行されていない。
*   以前のDaprプロセスが正しく終了せず、中途半端な状態になっている。
*   Docker Desktopが起動していない、またはDaprコンテナの起動に失敗している。

## 解決のための提案と次のステップ

問題を解決するために、以下の手順を試すことを提案します。これにより、ローカルのDapr環境をクリーンな状態にリセットし、問題を解消できる可能性が高いです。

1.  **Daprのステータス確認:**
    まず、現在のDaprの状態を確認します。ターミナルで以下のコマンドを実行してください。
    ```bash
    dapr status
    ```
    `dapr-placement` サービスが `Running` 状態でない場合、コントロールプレーンが正常に動作していないことが確定します。

2.  **Daprのアンインストールと再初期化:**
    Dapr環境を完全にリセットするために、以下のコマンドを順に実行します。
    ```bash
    # 既存のDaprコントロールプレーンを削除します
    dapr uninstall
    
    # Daprを再初期化します（Docker Desktopが起動していることを確認してください）
    dapr init
    ```

3.  **アプリケーションの再実行:**
    Daprの再初期化が正常に完了したら、再度アプリケーションの起動を試みます。
    ```bash
    cd internalUsages/DaprSample2
    dapr run \
      --app-id counter-demo \
      --app-port 5003 \
      --dapr-http-port 3501 \
      --dapr-grpc-port 50002 \
      --resources-path ./dapr-components \
      -- dotnet run --urls "http://localhost:5003"
    ```

これで、Daprサイドカーが正常にPlacementサービスに接続できるようになり、アプリケーションが正しく動作することが期待されます。✨
