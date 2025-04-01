先回作った

src/Sekiban.Pure/Projectors/IAggregateProjector.cs

ですが間違っています。
この方法で取得すべきではありません。
```間違っている例
    /// <summary>
    /// 型名からペイロードの型を取得します。
    /// </summary>
    /// <param name="payloadTypeName">ペイロード型の名前</param>
    /// <returns>見つかった型、または見つからない場合はnull</returns>
    public virtual Type? GetPayloadTypeByName(string payloadTypeName)
    {
        // 実装クラスのアセンブリ内で型名を検索
        return GetType().Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == payloadTypeName && 
                            typeof(IAggregatePayload).IsAssignableFrom(t));
    }
```

src/Sekiban.Pure/Aggregates/IAggregateTypes.cs
はSource Generatorで必要な機能を作ることができます。現時点で文字列から型を取得する機能はありませんが、必要でしたら作成して、継承クラスをSource Generator
src/Sekiban.Pure.SourceGenerator/AggregateTypesGenerator.cs
で作成可能です。

その方針で修正を設計してください。
以上が行いたいことですが、設計を考えて、どのような変更を行うかの方針を具体的にこのファイルに追記してください。

clinerules_bank/tasks/issue583_04_fix.md

わからないことは質問してその答えを得てから進めてください。わからないことを勝手に進めても成功しません。

実装方針を書き込んだら一旦終了してください。レビューをしてよければ、実装方針の通り進めるようにこちらから依頼します。

この行の上は削除しないでください。
ーーーーーーーーーーーーーーーーーーーーーーーー以下の行に追記可能-------------------

## 実装方針: ペイロード型名から型取得機能をSource Generatorで実装する

### 問題概要
現在、`IAggregateProjector`インターフェースには`GetPayloadTypeByName`メソッドのデフォルト実装が含まれています。このメソッドは、型名からペイロードの型を取得するためにリフレクションを使用していますが、この実装には以下の問題があります：

1. **パフォーマンス問題**: リフレクションでアセンブリ内の全ての型をスキャンするのは効率的ではない
2. **信頼性問題**: 単純な名前マッチングに依存しているため、同名の異なるクラスが存在する場合に誤った型を返す可能性がある
3. **設計上の問題**: 型解決の責任は`IAggregateProjector`ではなく、型管理を担当するクラスに属するべき

### 解決策の概要
Source Generatorを使用して、コンパイル時に型情報を収集し、型名から型への効率的なマッピングを提供する機能を実装します。具体的には：

1. `IAggregateTypes`インターフェースに型名から型を取得するメソッドを追加
2. `AggregateTypesGenerator`を拡張して、生成されるクラスに上記メソッドの実装を含める
3. `IAggregateProjector`の`GetPayloadTypeByName`実装を修正し、`IAggregateTypes`のインスタンスを使用する

### 実装詳細

#### 1. `IAggregateTypes`インターフェースの拡張

```csharp
// src/Sekiban.Pure/Aggregates/IAggregateTypes.cs
public interface IAggregateTypes
{
    public ResultBox<IAggregate> ToTypedPayload(Aggregate aggregate);
    public List<Type> GetAggregateTypes();
    
    // 新たに追加するメソッド
    public Type? GetPayloadTypeByName(string payloadTypeName);
}
```

#### 2. `AggregateTypesGenerator`の拡張

Source Generatorを修正して、型名から型へのマッピングを行うメソッドを生成するようにします。

```csharp
// GenerateSourceCode メソッド内に追加する生成コード
sb.AppendLine("        public Type? GetPayloadTypeByName(string payloadTypeName)");
sb.AppendLine("        {");
sb.AppendLine("            return payloadTypeName switch");
sb.AppendLine("            {");

// 各ペイロード型に対するマッピングを生成
foreach (var type in eventTypes)
{
    if (type.InterfaceName == "IAggregatePayload")
    {
        // 型の短い名前（NamespaceなしのClass名）を取得
        var shortName = type.RecordName.Split('.').Last();
        sb.AppendLine($"                \"{shortName}\" => typeof({type.RecordName}),");
    }
}

// デフォルトケース
sb.AppendLine("                _ => null");
sb.AppendLine("            };");
sb.AppendLine("        }");
```

#### 3. `IAggregateProjector`の修正

`IAggregateProjector`から`GetPayloadTypeByName`メソッドを削除します。これにより、タイプ解決は`IAggregateTypes`の正しい実装に委譲されます。

```csharp
// src/Sekiban.Pure/Projectors/IAggregateProjector.cs
public interface IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev);
    public virtual string GetVersion() => "initial";
    
    // GetPayloadTypeByName メソッドは削除
}
```

#### 4. `SerializableAggregate`クラスでの型解決方法の変更

`SerializableAggregate`クラス（前回のタスクで実装）では、型解決に`projector.GetPayloadTypeByName()`ではなく`SekibanDomainTypes`内の`AggregateTypes`を利用するように変更します。

```csharp
// 型解決の例
public async Task<OptionalValue<Aggregate>> ToAggregateAsync(
    SekibanDomainTypes domainTypes,
    JsonSerializerOptions options)
{
    // プロジェクターに依存せず、AggregateTypesから型を解決
    var payloadType = domainTypes.AggregateTypes.GetPayloadTypeByName(PayloadTypeName);
    if (payloadType == null)
    {
        return OptionalValue<Aggregate>.None;
    }
    
    // 後続の処理...
}
```

### テスト戦略

1. **ユニットテスト**: 生成された`GetPayloadTypeByName`メソッドのテストケースを追加し、正しく型を解決できることを確認
2. **統合テスト**: シリアライズ・デシリアライズの流れで型解決が正しく機能することをテスト
3. **エッジケース**: 存在しない型名や無効な型名に対して正しく`null`を返すことを確認

### 期待される利点

1. **パフォーマンス向上**: リフレクションによるランタイム検索からコンパイル時生成の直接マッピングへの変更
2. **型安全性の向上**: コンパイル時に型の存在が保証される
3. **責任の明確化**: 型解決の責任が適切なクラス（`IAggregateTypes`）に移動
4. **テスト容易性**: モックやスタブによる型解決のテストが容易になる

### 移行戦略

この変更はインターフェースの変更を伴うため、既存のコードの一部が影響を受ける可能性があります。以下の移行戦略を提案します：

1. まず`IAggregateTypes`インターフェースに新しいメソッドを追加
2. Source Generatorを更新し、既存のコードが新しいメソッドを使用できるようにする
3. 既存の`SerializableAggregate`クラスを更新して新しい方法で型解決を行う
4. 最後に`IAggregateProjector`から古いメソッドを削除

各ステップで単体テストを実行し、機能が正しく動作することを確認します。
