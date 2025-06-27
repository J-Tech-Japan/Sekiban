#!/bin/bash

# DaprSample2 Counter Actor テストスクリプト

BASE_URL="http://localhost:5003"
COUNTER_ID="test1"

echo "=== DaprSample2 Counter Actor テスト ==="
echo

# ヘルスチェック
echo "1. ヘルスチェック"
curl -s "$BASE_URL/health" | jq .
echo
echo

# 初期値を取得
echo "2. 初期カウンター値を取得"
curl -s "$BASE_URL/counter/$COUNTER_ID" | jq .
echo
echo

# インクリメント（3回）
echo "3. カウンターをインクリメント（3回）"
for i in {1..3}; do
    echo "  インクリメント $i:"
    curl -s -X POST "$BASE_URL/counter/$COUNTER_ID/increment" | jq .
    echo
done
echo

# 現在の値を確認
echo "4. 現在のカウンター値を確認"
curl -s "$BASE_URL/counter/$COUNTER_ID" | jq .
echo
echo

# リセット
echo "5. カウンターをリセット"
curl -s -X POST "$BASE_URL/counter/$COUNTER_ID/reset" | jq .
echo
echo

# リセット後の値を確認
echo "6. リセット後のカウンター値を確認"
curl -s "$BASE_URL/counter/$COUNTER_ID" | jq .
echo
echo

# 別のActorでテスト
COUNTER_ID2="test2"
echo "7. 別のActor ($COUNTER_ID2) でテスト"
echo "  初期値:"
curl -s "$BASE_URL/counter/$COUNTER_ID2" | jq .
echo
echo "  インクリメント:"
curl -s -X POST "$BASE_URL/counter/$COUNTER_ID2/increment" | jq .
echo
echo

# 両方のActorの値を確認
echo "8. 両方のActorの値を確認"
echo "  $COUNTER_ID:"
curl -s "$BASE_URL/counter/$COUNTER_ID" | jq .
echo
echo "  $COUNTER_ID2:"
curl -s "$BASE_URL/counter/$COUNTER_ID2" | jq .
echo

echo "=== テスト完了 ==="
