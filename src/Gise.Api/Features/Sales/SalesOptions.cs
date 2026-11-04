namespace Gise.Api.Features.Sales;

internal sealed class SalesOptions
{
    public const string Section = "Sales";

    /// <summary>How long chosen places stay set aside before checkout.</summary>
    public int HoldMinutes { get; set; } = 8;

    /// <summary>How long someone let in from a waiting room has to choose places.</summary>
    public int ShoppingMinutes { get; set; } = 15;

    /// <summary>Requests to take places, per person per minute: plenty for a person, slow for a bot.</summary>
    public int HoldsPerMinute { get; set; } = 30;

    /// <summary>How often unpaid orders, run-out holds and refunds are swept up; 0 turns the background sweep off.</summary>
    public int ReconcileSeconds { get; set; } = 15;

    /// <summary>How long after a payment deadline the provider is asked how it ended, for webhooks that are late.</summary>
    public int PaymentGraceSeconds { get; set; } = 60;
}
