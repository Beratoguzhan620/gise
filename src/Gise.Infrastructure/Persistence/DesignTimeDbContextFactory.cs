using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gise.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GiseDbContext>
{
    public GiseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GiseDbContext>();
        DependencyInjection.ConfigureDbContext(options, DependencyInjection.BuildDataSource("Host=localhost;Database=gise_design"));
        return new GiseDbContext(options.Options);
    }
}
