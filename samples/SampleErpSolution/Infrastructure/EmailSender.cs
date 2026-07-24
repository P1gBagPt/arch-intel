using Microsoft.Extensions.Options;

namespace SampleErp.Infrastructure;

public sealed class EmailSender
{
    private readonly SmtpSettings _settings;

    public EmailSender(IOptions<SmtpSettings> options)
    {
        _settings = options.Value;
    }

    public Task SendOrderConfirmationAsync(Guid orderId) => Task.CompletedTask;
}
