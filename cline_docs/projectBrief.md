# Project Brief
this is event sourcing C# framework. This contains two framework, which is Sekiban.Pure and Sekiban.Core.
I am developing Sekiban.Pure.* now which is in 
[folders](./src/Sekiban.Pure*)
maily please edit those.

# Current Issue.
I want to add to remove all feature in 
src/Sekiban.Pure/Events

First, I want to add IEventRemover in 
src/Sekiban.Pure/Events
Which has RemoveAllEvents()

Then implement IEventRemover to the src/Sekiban.Pure/Events/InMemoryEventWriter.cs

And Add tests/Pure.Domain.Test to make unit test for if RemoveAllEvents works.