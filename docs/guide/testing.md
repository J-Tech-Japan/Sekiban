# テストに関して

Sekibanプロジェクトのテストに関して、いくつかのアプローチがあります。この記事でどのようなアプローチがあるのか、また各プロジェクトでどのアプローチを選ぶことができるかを説明します。

## テストの種類

1. 実際にCosmodDBに接続してデータを確認するテスト

こちらが実際の状況に一番近いテストとなります。イベントが複数同時に保存されても正しく保存されるのか？スナップショットが正しく保存されるのか？保存したスナップショットから正く集約を復帰できるのかなど、下回りの動作確認を行います。しかし、各集約毎にこの形でテストしないといけないというものではなく、動作も時間がかかります。

参照
https://github.com/J-Tech-Japan/JJ_Sekiban/blob/0.3.2/test/SampleProjectStoryXTest/Stories/CustomerDbStoryBasic.cs


2. インメモリのデータベースを使用したストーリーテスト。

こちらは、データをCosmosDBに保存する代わりに、メモリ内に保存することにより、ストーリーテストを高速に行うことができるテスト方法です。この方法を使うことにより、基本的なコマンドおよびイベントの流れ、またMediatRを使用したイベントのPub/Subによるイベントドリブンの動作まで確認することができます。
このテストを使うことにより、基本的なCosmosDBクライアントに対する動作をテストすることができます。このテストは速度が速く、マルチスレッドに対応した記述をしているため、CosmosDBを対象にした動作で起きる問題が起きない可能性はあります。また、SnapshotやHybridなども対応していません。こちらのCosmodDBおよびSekiban内部の問題は、1)のテストで発見、対策されるべきですので、通常のクライアントはこちらのテストを使用できます。

https://github.com/J-Tech-Japan/JJ_Sekiban/blob/main/test/SampleProjectStoryXTest/Stories/InMemoryStoryTestBasic.cs
ちなみに上記のテストは、1.のCosmosDBを使ったテストと基本的に同じコードです。（DIの初期化処理のみ調整しています。）そのため正しく設定すれば、テストを書いている時に基本的にインメモリでテストを実行しつつ、時々CosmosDB相手にテストをすることなども可能です。

3. 単集約のみの挙動のユニットテスト Given When Then Expect テスト

こちらはイベントソーシングコミュニティでよく使用されているテストの形式です。
https://buildplease.com/pages/fpc-14/
https://github.com/heynickc/AggregateSource
こちらは、以下の要素を簡単に記述できるヘルパー機能を作成することにより、集約毎のテストを簡単に記述できるようにするテストの形式です。BDD 振る舞い駆動開発のGWTメソッドで作成しています
- Given (与えられるもの)テストする前までに生成されたはずのイベント、スナップショットを集約に与えることにより、テストの準備をする
- When コマンドを実行するか集約のパブリックメソッドをパラメーターを渡して叩く
- Then コマンドによって発生したイベント、ステータス、例外を確認して正しいかチェックする

https://github.com/J-Tech-Japan/JJ_Sekiban/blob/main/test/SampleProjectStoryXTest/SingleAggregates/ClientSpec.cs

上記にサンプルコードを記述しています。今のところはとても簡単なテストですが、集約の機能が増えてきた時に、まず集約で正しくイベントが生成されるか、それによってステータスがどのように変化するかを確認することが可能です。
記法に関しては、クラス内にメソッドがあるため、ドット記法で書くことも、ドット記法をつかわない方法も可能です。


```aggregateTest.cs
public class ClientSpec : SampleSingleAggregateTestBase<Client, ClientDto>
{ 
    private const string testClientName = "TestName";
    private const string testClientChangedName = "TestName2";
    private const string testEmail = "test@example.com";
    private const string testClientChangedNameV3 = "TestName3";

    [Fact(DisplayName = "集約コマンドを実行してテストする")]
    public void ClientCreateSpec()
    {
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<AggregateDtoBase> { branchDto });
        // CreateClient コマンドを実行する
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail));
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent(client => new ClientCreated(client.AggregateId, branchDto.AggregateId, testClientName, testEmail));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new ClientDto
            {
                AggregateId = client.AggregateId,
                BranchId = branchDto.AggregateId,
                ClientEmail = testEmail,
                ClientName = testClientName,
                Version = client.Version
            });
        // 名前変更コマンドを実行する
        WhenChange(client => new ChangeClientName(client.AggregateId, testClientChangedName) { ReferenceVersion = client.Version });
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new ClientDto
            {
                AggregateId = client.AggregateId,
                BranchId = branchDto.AggregateId,
                ClientEmail = testEmail,
                ClientName = testClientChangedName,
                Version = client.Version
            });
    }
    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        var clientDto = new ClientDto
        {
            AggregateId = Guid.NewGuid(),
            ClientName = "NOT DUPLICATED NAME",
            ClientEmail = testEmail,
            BranchId = Guid.NewGuid(),
            Version = 1
        };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<AggregateDtoBase> { branchDto, clientDto });
        // CreateClient コマンドを実行する エラーになるはず
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }
    [Fact(DisplayName = "コマンドではなく、集約メソッドをテストする")]
    public void UsingAggregateFunctionNoCommand()
    {
        var branchId = Guid.NewGuid();

        WhenConstructor(() => new Client(branchId, testClientName, testEmail))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientCreated(client.AggregateId, branchId, testClientName, testEmail))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientName,
                    Version = client.Version
                })
            .WhenMethod(
                aggregate =>
                {
                    aggregate.ChangeClientName(testClientChangedName);
                })
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientChangedName,
                    Version = client.Version
                });
    }
    [Fact(DisplayName = "イベントを渡してスタートする")]
    public void StartWithEvents()
    {
        var branchId = Guid.NewGuid();
        Given(new ClientCreated(Guid.NewGuid(), branchId, testClientName, testEmail))
            .Given(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            .WhenMethod(client => client.ChangeClientName(testClientChangedNameV3))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedNameV3))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientChangedNameV3,
                    Version = client.Version
                });
    }
    [Fact(DisplayName = "スナップショットを使用してテストを開始")]
    public void StartWithSnapshot()
    {
        var branchId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        Given(
                new ClientDto
                {
                    AggregateId = clientId,
                    BranchId = branchId,
                    ClientName = testClientName,
                    ClientEmail = testEmail,
                    Version = 1
                })
            .WhenMethod(client => client.ChangeClientName(testClientChangedName))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientChangedName,
                    Version = client.Version
                });
    }
}
```

# まとめ
Sekiban開発者は、主に 1. の機能をテストし、Sekibanを使用してアプリケーションを開発する開発者は主に、２、３をテストします。２、３の配分に関しては、プロジェクトによると思います。複雑な集約に関しては、３を重点的に、簡易な集約に関しては、２により基本動作と他の集約との連携を確認できるかと思います。



