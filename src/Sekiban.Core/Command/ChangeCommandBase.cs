﻿using Sekiban.Core.Aggregate;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Command;

public abstract record ChangeCommandBase<T> : ICommand where T : IAggregatePayload
{
    [Required]
    [Description("コマンドの対象となる集約のバージョン(この数字を利用して必要な場合楽観ロックを実施します)")]
    public int ReferenceVersion { get; init; }

    public abstract Guid GetAggregateId();
}