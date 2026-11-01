# Sekiban DCB 10.23.4

- バージョン: `10.23.4`。DCB ライブラリは 26 パッケージです。net9.0 と net10.0 を対象とし、`Microsoft.Orleans.*` は 10.3.1 のままです。
- データやスキーマの移行は不要で、公開 API の変更もありません。

## 修正

- **タグの予約中にキャンセルしても、予約が残らないようになりました。** タグの予約の途中でコマンドがキャンセルされると、成功した予約が期限（既定の設定では 30 秒）まで残り、そのタグへの他の書き込みが拒否されていました。今回から、成功した予約を取り消します。`ExecuteAsync` と serialized commit の両方が対象です。取り消しの呼び出し自体が失敗した場合は、その予約は期限まで残ります。
- **Orleans で、予約の呼び出しの失敗がログに出るようになりました**（issue #1349）。tag-consistent actor への取り消し・確定・通知の呼び出しが例外を投げた場合や、actor を取得できなかった場合、その失敗は多くても `Debug.WriteLine` にしか出ず、Release ビルドでは消えていました。Orleans では、host が `ILoggerFactory` を提供していれば、logger のカテゴリ `Sekiban.Dcb.Actors.TagReservationHelper` の warning として出ます。内容は、操作、タグ、予約コード（ある場合）です。`false` を返した呼び出しはログに出ません。コマンドの結果は変わりません。

## 改善

- **PostgreSQL: tag-head のロックを持っている間の処理を減らしました**（issue #1347 の一部）。タグの予想位置を指定した追記では、EF Core の自動 savepoint を作らなくなりました。追記が失敗したときは、これまでどおりイベントを書き込みません。
- **serialized commit がタグの予約を並行して要求するようになりました。** `ExecuteAsync` はすでにそうなっていました。

## アップグレード前の確認

- キャンセルされたコマンドは、実行中の予約の要求が終わるのを待つようになりました。予約を取り消すためです。予約の要求が遅いと、キャンセルの戻りもその分だけ遅くなります。
- このキャンセルでは `OperationCanceledException` を返すようになりました。`ExecuteAsync` ではこれまで、その派生クラスの `TaskCanceledException` でした。`TaskCanceledException` だけを判定しているコードは更新してください。
- キャンセルの途中で予約が拒否された場合は、`ExecuteAsync` でも serialized commit でも、既存の `InvalidOperationException`（"Failed to reserve tags"）を返します。
- in-memory など他の actor accessor では、これらの失敗を `Debug.WriteLine` に書かなくなり、代わりのログもありません。
- warning で通知する設定にしている場合は、上記のカテゴリの新しい warning が届くことがあります。

## テンプレート

10.23.4 のテンプレートは、ライブラリの後に、別途リリースします。
