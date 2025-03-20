AspireEventSample.ApiService/Grains/IBranchEntityPostgresWriter.cs

これはPostgres用のデータベース書き込み機能です。 

AspireEventSample.ApiService/Grains/BranchEntityWriter.cs
を真似して、 BranchEntityPostgresWriter を実装してください。

NPGSql+EntityFramework, BranchテーブルのみのDbContextを作ってください。

AspireEventSample.AppHost/Program.cs　
で ReadModel というPostgresデータベースを作成して、Aspireで取得したconnection string でDbContextに接続してください。

コードをできるだけ調べて、下記に日本語で具体的な実装プランを記述してください。
-----実装プラン------