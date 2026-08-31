namespace RentalManager.Infrastructure.Line;

public sealed class LineOptions
{
    public bool Enabled { get; set; }
    public string ChannelSecret { get; set; } = "";
    public string ChannelAccessToken { get; set; } = "";
}
