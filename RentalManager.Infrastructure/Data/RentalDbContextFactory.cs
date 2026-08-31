using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RentalManager.Infrastructure.Data;

public sealed class RentalDbContextFactory : IDesignTimeDbContextFactory<RentalDbContext>
{
    public RentalDbContext CreateDbContext(string[] args)
    {
        // คำสั่ง EF ที่ต้องเชื่อมต่อฐานจริงให้รับ connection string จาก environment
        // ส่วน fallback มีไว้สร้าง/เทียบ model เท่านั้น และไม่มีรหัสผ่านตัวอย่างฝังใน source code
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__RentalDb")
            ?? "Server=localhost;Database=RentalManager;Integrated Security=true;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new RentalDbContext(options);
    }
}
