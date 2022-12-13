
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Event;

namespace Sekiban.Core.Command
{
    public record CommandExecutorResponseWithEvents(Guid? AggregateId, Guid? CommandId, int Version,
        IEnumerable<ValidationResult>? ValidationResults, ImmutableList<IEvent> Events, string? LastSortableUniqueId) : CommandExecutorResponse(AggregateId, CommandId, Version, ValidationResults, LastSortableUniqueId)
    {
        public CommandExecutorResponseWithEvents() : this(null, null, 0, null, ImmutableList<IEvent>.Empty, null)
        {
        }
        public CommandExecutorResponseWithEvents(CommandExecutorResponse response, ImmutableList<IEvent> events) : this(response.AggregateId, response.CommandId, response.Version, response.ValidationResults, events, response.LastSortableUniqueId)
        {
        }
    }
}
