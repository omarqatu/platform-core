using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Notifications;

public sealed class NotificationDispatchOptions
{
    /// <summary>Messages waiting to be handed to a provider. Full = the oldest is dropped, loudly in the log.</summary>
    public int Capacity { get; set; } = 1024;
}

/// <summary>
/// Handing a message to the sender <b>without waiting for the provider</b> — the mechanism that makes the
/// constant-time <c>forgot</c> of OPEN_ITEMS 51 reachable.
/// <para>
/// The leak the item records is a timing one: an address with an account costs a token insert and a provider
/// round trip, an address without one costs nothing, and the difference is visible however uniform the 204 is.
/// A provider round trip is tens to hundreds of milliseconds and is not ours to bound, so no floor can hide it
/// honestly. Taking the send off the response path leaves only the database work, which is small and bounded,
/// and a floor can then cover it — see <c>PasswordPaths.BeginResetAsync</c> and the floor the endpoint holds.
/// </para>
/// <para>
/// <see cref="Enqueue"/> never blocks and never throws: it is called on a path whose answer must not depend on
/// what it did. A full queue drops the message and says so in the log.
/// </para>
/// </summary>
public interface INotificationDispatch
{
    bool Enqueue(NotificationMessage message);
}

public sealed class NotificationDispatch : INotificationDispatch
{
    private readonly Channel<NotificationMessage> queue;
    private readonly ILogger<NotificationDispatch> log;

    public NotificationDispatch(IOptions<NotificationDispatchOptions> options, ILogger<NotificationDispatch> log)
    {
        this.log = log;
        queue = Channel.CreateBounded<NotificationMessage>(new BoundedChannelOptions(options.Value.Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    internal ChannelReader<NotificationMessage> Reader => queue.Reader;

    public bool Enqueue(NotificationMessage message)
    {
        // DropOldest means TryWrite only fails once the channel is completed, which happens at shutdown alone.
        if (queue.Writer.TryWrite(message))
            return true;
        log.LogWarning("A {Channel} message was not queued: the dispatcher is shutting down.", message.Channel);
        return false;
    }
}

/// <summary>
/// Drains <see cref="NotificationDispatch"/> and calls the sender for each message, one at a time. A delivery
/// that fails is logged and dropped: these are the direct sends of §5.5 (invitations, passwords), which carry no
/// outbox row to retry from — the person asks again. Nothing here logs a recipient's address.
/// </summary>
public sealed class NotificationDispatcher(
    NotificationDispatch dispatch,
    IEnumerable<INotificationSender> senders,
    ILogger<NotificationDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in dispatch.Reader.ReadAllAsync(stoppingToken))
        {
            var sender = senders.FirstOrDefault(s => s.Channel == message.Channel);
            if (sender is null)
            {
                log.LogError("No sender is registered for the {Channel} channel; the message was dropped.", message.Channel);
                continue;
            }

            try
            {
                var delivery = await sender.SendAsync(message, stoppingToken);
                if (!delivery.Sent)
                    log.LogWarning("A {Channel} message was not delivered: {Error}", message.Channel, delivery.Error);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                // A sender must not throw; if one does, it must not take the dispatcher down with it.
                log.LogError(error, "The {Channel} sender threw; the message was dropped.", message.Channel);
            }
        }
    }
}
