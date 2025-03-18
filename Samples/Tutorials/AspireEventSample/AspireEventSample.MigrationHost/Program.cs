﻿﻿﻿using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AspireEventSample.MigrationHost;

// This is a design-time factory for the BranchDbContext
// It's used by the EF Core tools to create migrations
public class BranchDbContextFactory : IDesignTimeDbContextFactory<BranchDbContext>
{
    public BranchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BranchDbContext>();
        // Use a connection string that works for migrations
        // This is only used at design time for creating migrations
        optionsBuilder.UseNpgsql("Host=localhost;Database=ReadModel;Username=postgres;Password=postgres",
            b => b.MigrationsAssembly("AspireEventSample.MigrationHost"));

        return new BranchDbContext(optionsBuilder.Options);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        // Simple program that just prints a message
        Console.WriteLine("Migration Host - Use this project to create migrations for the BranchDbContext");
        Console.WriteLine("Example: dotnet ef migrations add InitialCreate --context BranchDbContext");
    }
}
