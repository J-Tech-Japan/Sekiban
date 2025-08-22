using Xunit;

// 並列実行を無効化し1クラスタずつ起動を確認
[assembly: CollectionBehavior(DisableTestParallelization = true)]
