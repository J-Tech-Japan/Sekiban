using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.Actors;

internal interface ITagReservationLoggerSource
{
    ILogger TagReservationLogger { get; }
}
