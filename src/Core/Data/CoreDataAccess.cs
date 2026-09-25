using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Core.Data;

public static class CoreDataAccess
{
    /// <summary>
    /// The app_user data source. Connection state is reset when a connection
    /// returns to the pool (3.4) — Npgsql's default, enforced here so a
    /// connection string cannot switch it off.
    /// </summary>
    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        if (builder.ConnectionStringBuilder.NoResetOnClose)
            throw new InvalidOperationException(
                "No Reset On Close must stay off: pooled connections are reset on return (PLATFORM_CORE 3.4).");
        return builder.Build();
    }

    /// <summary>Applies the transaction layer and automatic auditing (7) to any context built on CoreDbContext.</summary>
    public static DbContextOptionsBuilder UseCoreDataAccess(this DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource)
            .AddInterceptors(TransactionRequiredInterceptor.Instance, AutomaticAuditInterceptor.Instance);

    public static IServiceCollection AddCoreDataAccess(this IServiceCollection services, string appUserConnectionString)
    {
        var dataSource = CreateDataSource(appUserConnectionString);
        services.AddSingleton(dataSource);
        services.AddDbContext<CoreDbContext>(options => options.UseCoreDataAccess(dataSource));
        return services;
    }
}
