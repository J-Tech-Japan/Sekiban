using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Event;

namespace Sekiban.Core.PubSub
{
    public interface IEventSubscriber<TEventPayload> where TEventPayload : IEventPayloadCommon
    {
        public Task HandleEventAsync(Event<TEventPayload> ev);
    }
}
