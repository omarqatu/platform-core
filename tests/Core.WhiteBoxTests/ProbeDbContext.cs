using System.Data.Common;
using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Core.WhiteBoxTests;

public sealed class Probe
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Label { get; set; } = "";
    public int Counter { get; set; }
}

/// <summary>A test-only context over t1_probe, built with Core's transaction layer.</summary>
public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : CoreDbContext(options)
{
    public DbSet<Probe> Probes => Set<Probe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Probe>(probe =>
        {
            probe.ToTable("t1_probe");
            probe.Property(p => p.Id).HasColumnName("id");
            probe.Property(p => p.TenantId).HasColumnName("tenant_id");
            probe.Property(p => p.Label).HasColumnName("label");
            probe.Property(p => p.Counter).HasColumnName("counter");
        });

    public static ProbeDbContext Create(NpgsqlDataSource dataSource, params IInterceptor[] extra)
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>();
        Configure(options, dataSource, extra);
        return new ProbeDbContext(options.Options);
    }

    public static void Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource, params IInterceptor[] extra)
    {
        options.UseCoreDataAccess(dataSource);
        if (extra.Length > 0)
            options.AddInterceptors(extra);
    }
}

/// <summary>Records every command's text, without touching it.</summary>
public sealed class CommandRecorder : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands
    {
        get { lock (_commands) return _commands.ToList(); }
    }

    public void Clear()
    {
        lock (_commands) _commands.Clear();
    }

    private void Record(DbCommand command)
    {
        lock (_commands) _commands.Add(command.CommandText);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }
}
