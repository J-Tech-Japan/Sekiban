# Test GetStarted Solution with Cosmos DB

To test GetStarted solution, you need to have Cosmos DB. 

## Prepare for the Cosmos DB

You can use either local emulator or use Azure Cosmos DB. Sekiban uses new [Hierarchical Partition Key](https://learn.microsoft.com/en-us/azure/cosmos-db/hierarchical-partition-keys?tabs=net-v3%2Cbicep) feature for the Cosmos DB, so developer needs to be aware to use correctly.

Either way you used, you need to get following information.

- **URI**
- **Primary Key**

For Local Emulator in Windows, please see [This Page](prepare-cosmos-db-local.md)

For Azure Cosmos DB, please see [This Page](prepare-cosmos-db-azure.md)

## Prepare for Blob Storage (Optional)

Sekiban uses blob storage for following data.

- Aggregate Snapshot when size is big. 

    Azure Cosmos DB has 4MB data limit, but to be safe not to hit limit, sekiban will make snapshot blob when payload json is about to hit 1MB.

- Projection Snapshot.
    
    Projection snapshot is always created in the blob storage but default setting has set not to make snapshot below 3000 events.

Because case that uses blob is rare in the getting started level of the data, you don't need to create blob storage for getting started project. 


### Using Azure Cosmos DB.








1. Visual Studio 2022.
    
    1-1 Open `GetStarted.sln`
    1-2 
