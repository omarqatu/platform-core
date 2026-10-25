using System.Net;
using System.Net.Mail;
using Core.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Notifications;

/// <summary>
/// Where the e-mail sender reads what the database must not hold (Amendment 01 §2.6). The settings section is
/// <c>Notifications:Email</c>; the provider's own secret lives under <c>Providers:&lt;credential_ref&gt;</c>, and
/// <c>credential_ref</c> is the value the platform's <c>provider_accounts</c> row carries — the database names the
/// key, the settings hold the secret.
/// </summary>
public sealed class EmailSenderOptions
{
    /// <summary>§5.5: the provider is called with a 5 s timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The settings path the credentials sit under, by credential_ref.</summary>
    public string CredentialSection { get; set; } = "Notifications:Email:Providers";
}

/// <summary>One provider's credentials, as the settings hold them. Never written to the database, never logged.</summary>
public sealed class EmailProviderCredentials
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// The e-mail channel (Amendment 01 D3, §5.5), over SMTP. The account — which provider, which sender address,
/// which settings key — comes from the platform's <c>provider_accounts</c> row; the secret comes from the
/// settings key that row's <c>credential_ref</c> names, and never from the database (§2.6).
/// <para>
/// It does not throw for a refused delivery: a channel failing is a <c>failed</c> row in the outbox (§5.3), and on
/// the <c>forgot</c> path it must not disturb the uniform 204. Nothing it logs carries the recipient's address or
/// any part of the credentials — an address in a log is the enumeration the uniform 204 exists to prevent.
/// </para>
/// </summary>
public sealed class EmailNotificationSender(
    IPlatformProviderAccounts accounts,
    IConfiguration configuration,
    IOptions<EmailSenderOptions> options,
    ILogger<EmailNotificationSender> log) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<NotificationDelivery> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (message.Channel != NotificationChannel.Email)
            return NotificationDelivery.Failed($"this sender serves {NotificationChannel.Email}, not {message.Channel}");

        // Exactly one recipient, the one given (item 35). Measured before this line existed: "adnan" threw
        // FormatException out of new MailMessage, outside the try below — this class broke its own contract — and
        // "a@x.com, b@y.com" was parsed as two recipients and sent to both. Refused here, before any provider is
        // looked up, and loudly; never with the address, which no log here carries.
        if (!EmailAddress.IsSendable(message.Recipient))
        {
            log.LogError("The recipient of an {Channel} message is not one deliverable address; nothing was sent.", message.Channel);
            return NotificationDelivery.Failed("malformed recipient");
        }
        var recipient = new MailAddress(message.Recipient.Trim(' '));

        var account = await accounts.ForAsync(NotificationChannel.Email, cancellationToken);
        if (account is null)
        {
            log.LogError("No active platform provider_accounts row for the email channel; nothing was sent.");
            return NotificationDelivery.Failed("no active platform email provider");
        }

        var credentials = configuration
            .GetSection($"{options.Value.CredentialSection}:{account.CredentialRef}")
            .Get<EmailProviderCredentials>();
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.Host))
        {
            // The reference, never the secret: credential_ref is a settings key name, not a value (§2.6).
            log.LogError(
                "provider_accounts names the settings key {CredentialRef}, and it holds no usable credentials.",
                account.CredentialRef);
            return NotificationDelivery.Failed($"no credentials under {account.CredentialRef}");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Value.Timeout);

        // The parsed address, never the string: MailMessage(string, string) reads a string as a list.
        using var mail = new MailMessage(new MailAddress(account.SenderId), recipient)
        {
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false,
        };
        // Dormant today: Gmail SMTP rewrites no links, so no caller sets a header. Applied here rather than left
        // to each caller so that a provider change is one configuration decision and not a code hunt.
        foreach (var (name, value) in message.Headers ?? new Dictionary<string, string>())
            mail.Headers.Add(name, value);
        using var client = new SmtpClient(credentials.Host, credentials.Port)
        {
            EnableSsl = credentials.UseStartTls,
            Timeout = (int)options.Value.Timeout.TotalMilliseconds,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrEmpty(credentials.Username)
                ? null
                : new NetworkCredential(credentials.Username, credentials.Password),
        };

        try
        {
            await client.SendMailAsync(mail, deadline.Token);
            // SMTP returns no durable identifier of its own, so there is none to record: the outbox's
            // provider_message_id stays null for this channel, and the row still moves to 'sent' (§5.5).
            return NotificationDelivery.Delivered();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("The email provider did not answer within {Timeout}.", options.Value.Timeout);
            return NotificationDelivery.Failed($"the provider did not answer within {options.Value.Timeout}");
        }
        catch (SmtpException error)
        {
            log.LogWarning(error, "The email provider refused the message ({Status}).", error.StatusCode);
            return NotificationDelivery.Failed($"smtp: {error.StatusCode}");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            log.LogWarning(error, "The email provider could not be reached.");
            return NotificationDelivery.Failed("the provider could not be reached");
        }
    }
}
