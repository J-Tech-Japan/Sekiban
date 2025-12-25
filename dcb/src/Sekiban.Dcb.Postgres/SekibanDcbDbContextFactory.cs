using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Sekiban.Dcb.Postgres;

public class SekibanDcbDbContextFactory : IDesignTimeDbContextFactory<SekibanDcbDbContext>
{
    public SekibanDcbDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SekibanDcbDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=sekiban_dcb_design;Username=design_user;Password=design_pass");
        return new SekibanDcbDbContext(optionsBuilder.Options);
    }
}
