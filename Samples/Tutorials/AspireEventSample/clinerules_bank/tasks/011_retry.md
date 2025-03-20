What you suggested on 
clinerules_bank/tasks/010_orleans.md
is really bad.

AspireEventSample.ApiService/Grains/EventConsumerGrain.cs
should make instanse of 
AspireEventSample.ApiService/Aggregates/ReadModel/BranchReadModelHandler.cs
and 
AspireEventSample.ApiService/Aggregates/ReadModel/ShoppingCartReadModelHandler.cs
without DI.
you can DI 
IBranchWriter
and also use GrainFactory in EventConsumerGrain to instanciate
BranchEntityPostgresWriterGrain
and you need to pass
BranchEntityPostgresWriterGrain
to the EventProcessor

I don't want you to start changing code.
just suggest in this file
clinerules_bank/tasks/011_retry.md
what you would like to do.
