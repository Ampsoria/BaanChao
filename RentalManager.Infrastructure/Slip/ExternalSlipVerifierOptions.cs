namespace RentalManager.Infrastructure.Slip;

public sealed class ExternalSlipVerifierOptions
{
    public const string SectionName = "SlipVerification:External";
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
