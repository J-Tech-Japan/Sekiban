AspireEventSample.ApiService/Grains/EventConsumerGrain.cs
をリファクタリングしたい。

OnNextAsync の中で行っていることを、できれば汎用化して純粋関数化したい。

それによって、同じコードを使い回して、Readmodel Updatorを実行できるようにしたい

今やっている、OrleansのイベントストリーミングでRead Model を作成する方法
かつ、コンソールアプリで最初のイベントから今まで、もしくは過去の何処かから今までと実行できるようにしたい。

それを行うための抽象化を行いたいです。

つまり純粋関数を持つクラスを作り、

AspireEventSample.ApiService/Grains/EventConsumerGrain.cs
から呼び出す機能を作る。

ただ、このタスクでは計画するだけです。
このファイルの下部に計画をよく考えて記入してください。必要なファイルの読み込みなど調査を行い、できるだけ具体的に計画してください。

+++++++++++以下に計画を書く+++++++++++

