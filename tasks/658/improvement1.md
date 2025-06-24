
Orleans project has 2 grains.

src/Sekiban.Pure.Orleans/IAggregateEventHandlerGrain.cs
src/Sekiban.Pure.Orleans/IAggregateProjectorGrain.cs

This is defently need to implemented in the dapr version.

src/Sekiban.Pure.Orleans/IAggregateProjectorGrain.cs
for projecting aggregate and running command in the single grain.

and 
src/Sekiban.Pure.Orleans/IAggregateEventHandlerGrain.cs
for the persist and read for the events for each aggregate streams.

currently dapr version has only
src/Sekiban.Pure.Dapr/Actors/AggregateActor.cs
src/Sekiban.Pure.Dapr/Actors/IAggregateActor.cs

which is for 
src/Sekiban.Pure.Orleans/IAggregateProjectorGrain.cs

pleaes follor more in design pattern for orleans and improve dapr version of the library.