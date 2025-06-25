

すでにOrleansであるイベントソーシング実装をDapr版を作っています。
Actor の in / out に関してインターフェース型だと、シリアライズがうまくいきません。
https://github.com/J-Tech-Japan/Sekiban/blob/17520165dc74ebb6aded1d9254c6dea970f9f039/src/Sekiban.Pure.Dapr/Actors/IAggregateActor.cs#L31

そのため、アクターの入力は、エンベロープとペイロードを含んだシンプルな型にして、 SekibanDaprExecutor で変換したものを入れて欲しい。
開発者は SekibanDaprExecutor に対して、今まで通りの入力をするが、AggregateActor の入力は、決まった型のものとする。決まった型を取った AggregateActorでは、それを変換して型に戻してから使用したい。

さらに言うと、SekibanDaprExecutor に渡す方はprotobufの型でもいいかと考えています。
SekibanDaprExecutor 入力 - protobuf 
AggregateActor 入力 - protobuf をパックしたJSON (Dapr アクターはjsonベースのため)
AggregateActor の中で protobuf -> c# のコマンド
C#のコマンドではイベント C# を生成する
イベント C# に紐づいた protobuf をパックしたクラスをAggregateEventHandlerActor に送る

のようなフローを考えています。
それを含めた
src/Sekiban.Pure.Dapr
プロジェクトおよびそのテストケース
internalUsages/DaprSample
の設計をして下さい。

設計はチャットではなく、以下の新しいファイル
tasks/658/design3.[hhmmss 編集を開始した時間、分、秒].md
に現在の設計を書いてください。また、あなたのLLMモデル名もファイルの最初に書き込んでください。
