using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Core.Data;

/// <summary>
/// The loud upper layer (3.5): any command without an active transaction throws,
/// instead of reaching the policy's silent zero rows. It only inspects commands;
/// it never edits their text (3.5/3, the Npgsql batching pitfall).
/// </summary>
public sealed class TransactionRequiredInterceptor : DbCommandInterceptor
{
    public static readonly TransactionRequiredInterceptor Instance = new();

    private TransactionRequiredInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Require(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Require(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Require(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Require(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Require(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Require(command);
        return ValueTask.FromResult(result);
    }

    private static void Require(DbCommand command)
    {
        if (command.Transaction is null)
            throw new CommandOutsideTransactionException();
    }
}

public sealed class CommandOutsideTransactionException()
    : InvalidOperationException(
        "A database command ran without an explicit transaction. Every access goes through the unit of work (PLATFORM_CORE 3.5).");
