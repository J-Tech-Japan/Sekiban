src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs

の調整です。これはストリーム対応しようとしてバグとなっている状態なので、現在のコードの正しさは疑ってください。

やりたいことは
- Stream中に来たイベントは クラス内に保持する _buffer
- Stream中に来たイベントは基本的に _buffer に追記するだけで、_applyしなくていい
- BuildStateIfNeededAsync でStream中の場合、FlushBufferを行うが、FlushBuffer では以下を行う
    - _bufferから _safeStateにApplyする前に、SortableUniqueId でソートする
    - _safeState に _bufferから適用し終わったら、_bufferからは削除して良い
    - _safeStateに適用後残りの _buffer のイベントを _safeStateに適用してできたものが_unsafeState
    - query は _unsafeStateから返す



ただ、このタスクでは計画するだけです。
このファイルの下部に計画をよく考えて追記で記入してください。必要なファイルの読み込みなど調査を行い、できるだけ具体的に計画してください。
わからないことがあったら質問してください。わからない時に決めつけて作業せず、質問するのは良いプログラマです。

設計はチャットではなく、以下の新しいファイル

clinerules_bank/tasks/issue619_01_keep_all.[hhmmss 編集を開始した時間、分、秒].md

に現在の設計を書いてください。また、あなたのLLMモデル名もファイルの最初に書き込んでください。
