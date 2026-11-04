namespace Gise.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string InvalidState = "invalid_state";
    public const string Conflict = "conflict";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RateLimited = "rate_limited";
    public const string SaleNotOpen = "sale_not_open";
    public const string QueueRequired = "queue_required";
    public const string PlaceUnavailable = "place_unavailable";
    public const string TicketLimitReached = "ticket_limit_reached";
    public const string HoldExpired = "hold_expired";
    public const string PaymentUnavailable = "payment_unavailable";
    public const string RefundClosed = "refund_closed";
    public const string InvalidSignature = "invalid_signature";
}
