namespace OrderFlow.Contracts;

/// <summary>
/// The payment emulator has three behaviours, switched at runtime through one Redis key. Payments reads the key for
/// every payment; the admin UI (stage 4) writes it. Nothing else about payments is configurable on purpose.
/// </summary>
public static class PaymentEmulator
{
    public const string ModeKey = "payments:mode";

    /// <summary>The customer is charged.</summary>
    public const string Success = "success";

    /// <summary>The bank refuses the payment.</summary>
    public const string Decline = "decline";

    /// <summary>The bank answers too slowly: after a delay the payment is reported as failed (timed out).</summary>
    public const string Timeout = "timeout";
}
