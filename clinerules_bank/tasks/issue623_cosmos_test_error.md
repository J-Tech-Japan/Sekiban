info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      restore snapshot
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      catch up from store
fail: Orleans.Grain[0]
      Error thrown from OnActivateAsync for activation '[Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@b36d6b9556494c7dace1e3424a03da41#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]'.
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
fail: Orleans.Grain[100513]
      Error activating grain [Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@b36d6b9556494c7dace1e3424a03da41#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      restore snapshot
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      catch up from store
fail: Orleans.Grain[0]
      Error thrown from OnActivateAsync for activation '[Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@b7876897c0564b108233fc250e321b01#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]'.
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
fail: Orleans.Grain[100513]
      Error activating grain [Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@b7876897c0564b108233fc250e321b01#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      restore snapshot
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      catch up from store
fail: Orleans.Grain[0]
      Error thrown from OnActivateAsync for activation '[Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@7efe326db952482780a5cf2f4bbf9301#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]'.
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
fail: Orleans.Grain[100513]
      Error activating grain [Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@7efe326db952482780a5cf2f4bbf9301#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      restore snapshot
info: Sekiban.Pure.Orleans.Grains.MultiProjectorGrain[0]
      catch up from store
fail: Orleans.Grain[0]
      Error thrown from OnActivateAsync for activation '[Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@e3feca7f22f44ff892de4fc710acf20f#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]'.
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
fail: Orleans.Grain[100513]
      Error activating grain [Activation: S169.254.88.215:50552:106470587/multiprojector/AggregateListProjector`1+WeatherForecastProjector@e3feca7f22f44ff892de4fc710acf20f#GrainType=Sekiban.Pure.Orleans.Grains.MultiProjectorGrain,Sekiban.Pure.Orleans Placement=RandomPlacement State=Activating]
      System.ArgumentNullException: Value cannot be null. (Parameter 'authKeyOrResourceToken')
         at Microsoft.Azure.Cosmos.AuthorizationTokenProvider.CreateWithResourceTokenOrAuthKey(String authKeyOrResourceToken)
         at Microsoft.Azure.Cosmos.CosmosClient..ctor(String accountEndpoint, String authKeyOrResourceToken, CosmosClientOptions clientOptions)
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientFromUriAndKey() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 160
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetCosmosClientAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 149
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.GetContainerAsync(DocumentType documentType) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 167
         at Sekiban.Pure.CosmosDb.CosmosDbFactory.CosmosActionAsync[T](DocumentType documentType, Func`2 cosmosAction) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbFactory.cs:line 34
         at Sekiban.Pure.CosmosDb.CosmosDbEventReader.GetEvents(EventRetrievalInfo eventRetrievalInfo) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.CosmosDb/CosmosDbEventReader.cs:line 14
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.CatchUpFromStoreAsync() in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 227
         at Sekiban.Pure.Orleans.Grains.MultiProjectorGrain.OnActivateAsync(CancellationToken ct) in /Users/tomohisa/dev/GitHub/Sekiban/src/Sekiban.Pure.Orleans/Grains/MultiProjectorGrain.cs:line 80
         at Orleans.Runtime.ActivationData.ActivateAsync(Dictionary`2 requestContextData, CancellationToken cancellationToken) in /_/src/Orleans.Runtime/Catalog/ActivationData.cs:line 1663
