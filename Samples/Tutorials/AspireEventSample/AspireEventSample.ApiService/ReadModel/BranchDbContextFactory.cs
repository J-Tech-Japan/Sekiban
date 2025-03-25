using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace AspireEventSample.ApiService.ReadModel;

public class BranchDbContextFactory : IDesignTimeDbContextFactory<BranchDbContext>
{
    public BranchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BranchDbContext>();
        // Use a connection string that works for migrations
        // This is only used at design time for creating migrations
        optionsBuilder.UseNpgsql("Host=localhost;Database=ReadModel;Username=postgres;Password=postgres");

        return new BranchDbContext(optionsBuilder.Options);
    }
}