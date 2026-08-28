namespace HRSystem.Application.Interfaces;

/// <summary>A single email message to be sent.</summary>
public record EmailMessage(
    IEnumerable<string> To,
    string Subject,
    string HtmlBody,
    IEnumerable<string>? Cc = null);

/// <summary>
/// Sends emails. The default implementation queues sending via a background
/// job so request handling is not blocked by SMTP latency.
/// </summary>
public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Enqueue for background (non-blocking) delivery with retries.</summary>
    void Enqueue(EmailMessage message);
}
