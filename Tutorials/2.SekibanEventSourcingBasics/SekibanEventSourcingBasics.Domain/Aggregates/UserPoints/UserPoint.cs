using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Aggregate;

namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;

public record UserPoint(string Name, string Email, int Point) : IAggregatePayload<UserPoint>
{
    public static UserPoint CreateInitialPayload(UserPoint? _) => new UserPoint(string.Empty,string.Empty,0);
}