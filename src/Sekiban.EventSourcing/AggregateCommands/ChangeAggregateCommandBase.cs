using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.AggregateCommands;

public abstract record ChangeAggregateCommandBase<T> : IAggregateCommand where T : IAggregate
{

    [Required]
    [Description("コマンドの対象となる集約のバージョン(この数字を利用して必要な場合楽観ロックを実施します)")]
    public int ReferenceVersion { get; init; }
}
