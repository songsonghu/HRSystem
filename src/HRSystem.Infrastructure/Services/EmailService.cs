using HRSystem.Application.Interfaces;
using Hangfire;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HRSystem.Infrastructure.Services;

/// <summary>SMTP settings bound from configuration ("Smtp" section).</summary>
public class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool UseStartTls { get; set; } = false;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "hr-system@example.com";
    public string FromName { get; set; } = "HR System";
}

/// <summary>
/// Email service backed by MailKit. <see cref="Enqueue"/> schedules delivery on
/// a Hangfire background job with automatic retries so the request thread is
/// never blocked by SMTP latency.
/// </summary>
public class EmailService : IEmailService
{
    private readonly SmtpOptions _options;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpOptions> options, IBackgroundJobClient jobs, ILogger<EmailService> logger)
    {
        _options = options.Value;
        _jobs = jobs;
        _logger = logger;
    }

    public void Enqueue(EmailMessage message)
        => _jobs.Enqueue(() => SendCoreAsync(
            message.To.ToArray(), message.Subject, message.HtmlBody,
            (message.Cc ?? Array.Empty<string>()).ToArray(), CancellationToken.None));

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        => SendCoreAsync(message.To.ToArray(), message.Subject, message.HtmlBody,
            (message.Cc ?? Array.Empty<string>()).ToArray(), cancellationToken);

    /// <summary>
    /// Actual SMTP send. Public and parameter-serializable so Hangfire can
    /// invoke it as a background job.
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SendCoreAsync(string[] to, string subject, string htmlBody, string[] cc, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        foreach (var t in to) msg.To.Add(MailboxAddress.Parse(t));
        foreach (var c in cc) msg.Cc.Add(MailboxAddress.Parse(c));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var secure = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(_options.Host, _options.Port, secure, ct);
        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Email sent to {To} - {Subject}", string.Join(",", to), subject);
    }
}
