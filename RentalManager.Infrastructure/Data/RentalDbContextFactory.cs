using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RentalManager.Infrastructure.Data;

public sealed class RentalDbContextFactory : IDesignTimeDbContextFactory<RentalDbContext>
{
    public RentalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=RentalManager;User Id=sa;Password=DesignTimeOnly!123;TrustServerCertificate=True")
            .Options;
        return new RentalDbContext(options);
    }
}
