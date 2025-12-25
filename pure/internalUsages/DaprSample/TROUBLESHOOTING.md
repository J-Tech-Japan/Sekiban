# DaprSample Troubleshooting Guide

## "did not find address for actor" エラーの解決方法

このエラーは、DaprのPlacement Serviceがアクターのアドレスを解決できない場合に発生します。

### 原因と解決策

#### 1. Placement Serviceの接続確認
```bash
# Placement Serviceが正しく動作しているか確認
docker ps | grep placement

# ポート50005が開いているか確認（Mac/Linux）
lsof -i :50005
```

#### 2. Docker Desktopの設定（Mac環境）
- Docker Desktop → Settings → Resources → Network
- "Use default Docker subnet" を有効にする
- Docker Desktopを再起動

#### 3. Daprの再初期化
```bash
# Daprをクリーンアップして再初期化
dapr uninstall
dapr init --slim
```

#### 4. アクターIDプレフィックスの簡略化
`Program.cs`で以下のように変更してみてください：
```csharp
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-statestore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "domain-events";
    // options.ActorIdPrefix = "dapr-sample"; // この行をコメントアウト
});
```

#### 5. リトライロジックの実装
初回アクセス時にエラーが発生する場合、数秒待ってリトライすると成功することがあります：
```bash
# test-with-retry.sh
#!/bin/bash

USER_ID=$(uuidgen)
MAX_RETRIES=3
RETRY_DELAY=5

for i in $(seq 1 $MAX_RETRIES); do
    echo "Attempt $i of $MAX_RETRIES"
    
    RESPONSE=$(curl -s -X POST http://localhost:5000/api/users/create \
      -H "Content-Type: application/json" \
      -d "{\"UserId\": \"$USER_ID\", \"Name\": \"テストユーザー\", \"Email\": \"test@example.com\"}")
    
    echo "Response: $RESPONSE"
    
    if echo "$RESPONSE" | grep -q "\"success\":true"; then
        echo "Success!"
        break
    elif [ $i -lt $MAX_RETRIES ]; then
        echo "Retrying in $RETRY_DELAY seconds..."
        sleep $RETRY_DELAY
    fi
done
```

#### 6. ネットワークモードの変更（Docker Compose）
`docker-compose.yml`でPlacement Serviceのネットワークモードを変更：
```yaml
placement:
  image: "daprio/dapr"
  command: ["./placement", "--port", "50005"]
  ports:
    - "50005:50005"
  network_mode: "host"  # Macで問題がある場合はこれを試す
```

### デバッグ情報の収集

問題が続く場合は、以下のコマンドで詳細情報を収集してください：

```bash
# Daprのバージョン確認
dapr --version

# アクター設定の確認
curl http://localhost:5000/dapr/config

# Placement Serviceのログ確認
docker logs daprsample-placement-1

# Daprサイドカーのログ確認（詳細モード）
dapr run --app-id sekiban-api \
  --app-port 5000 \
  --log-level debug \
  --placement-host-address localhost:50005 \
  --resources-path ./dapr-components \
  -- dotnet run --project ./DaprSample.Api/DaprSample.Api.csproj
```

### Mac環境特有の注意事項

1. **localhost vs 127.0.0.1**: Macでは`localhost`の解決に問題がある場合があります。`127.0.0.1`を使用してみてください。

2. **ポートフォワーディング**: Docker DesktopのVMとホストマシン間のポートフォワーディングに問題がある場合があります。

3. **mDNS**: Daprのデフォルトのname resolutionはmDNSを使用します。Mac環境では明示的に`--name-resolution-component consul`などを指定する必要がある場合があります。