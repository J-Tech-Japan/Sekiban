using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sekiban.Core.Exceptions
{
    public class SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException : Exception, ISekibanException
    {
        public SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException(string message) : base(message) { }
    }
}
