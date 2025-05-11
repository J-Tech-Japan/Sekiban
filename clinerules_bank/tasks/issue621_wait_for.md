コマンド実行後にQueryすると、CQRSのため、最新でないという問題があります。

そのためにまず、WebでコマンドAPIを実行した時に、SortableUniqueIdを取れる様にしたい
https://github.com/J-Tech-Japan/Sekiban/blob/cc4e73eeafb0762840abf9f6d898338bab425bce/internalUsages/OrleansSekiban.ApiService/Program.cs#L288
ここではちゃんとCommandResponseを返しているので、型としても正しく返したい

かつ
https://github.com/J-Tech-Japan/Sekiban/blob/cc4e73eeafb0762840abf9f6d898338bab425bce/internalUsages/OrleansSekiban.Web/WeatherApiClient.cs#L28-L29
InputWeatherAsync
RemoveWeatherAsync
UpdateLocationAsync
でしっかりCommandResponseを取得したい

その上で、クエリに機能追加をするわけですが、クエリの基本機能に WaitForSortableUniqueId というのを追加できる様にする。そのために IWaitForSortableUniqueId を作り、そこで
WaitForSortableUniqueId string
を取得し、それがnullかからの場合は、何も待たない
WaitForSortableUniqueId
が入っているときは、Grainで実行する前に

https://github.com/J-Tech-Japan/Sekiban/blob/cc4e73eeafb0762840abf9f6d898338bab425bce/src/Sekiban.Pure.Orleans/Parts/SekibanOrleansExecutor.cs#L34-L35
https://github.com/J-Tech-Japan/Sekiban/blob/cc4e73eeafb0762840abf9f6d898338bab425bce/src/Sekiban.Pure.Orleans/Parts/SekibanOrleansExecutor.cs#L48
で待つ様にする

待つ方法は、対象のMultiProjectionを特定し、

src/Sekiban.Pure.Orleans/IMultiProjectorGrain.cs
に
bool IsSortableUniqueIdReceived(string)
を作り、
_bufferに持っているか、_safeState.LastSortableUniqueId の方が新しい場合は yesを返す様にする

ただ、このタスクでは計画するだけです。
このファイルの下部に計画をよく考えて追記で記入してください。必要なファイルの読み込みなど調査を行い、できるだけ具体的に計画してください。
わからないことがあったら質問してください。わからない時に決めつけて作業せず、質問するのは良いプログラマです。

設計はチャットではなく、以下の新しいファイル

clinerules_bank/tasks/issue621_wait_for.[hhmmss 編集を開始した時間、分、秒].md

に現在の設計を書いてください。また、あなたのLLMモデル名もファイルの最初に書き込んでください。
