namespace Core.Options;

public class HandlerOptions
{
    public const string Section = "Handlers";

    // 0.0 = always succeed, 1.0 = always fail
    public double SendEmailFailureRate       { get; set; } = 0.30;
    public double WebhookDeliveryFailureRate { get; set; } = 0.25;
    public double LogProcessorFailureRate    { get; set; } = 0.15;
}