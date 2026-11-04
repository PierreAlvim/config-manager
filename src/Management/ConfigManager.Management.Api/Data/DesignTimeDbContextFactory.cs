using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ConfigManager.Management.Api.Data;

/// <summary>Used by `dotnet ef` only.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=configmanager;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options);
}
