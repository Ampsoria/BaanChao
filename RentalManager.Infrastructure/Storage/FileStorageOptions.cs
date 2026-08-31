namespace RentalManager.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";
    public string SlipRoot { get; set; } = "slips";
    public int MaxUploadMegabytes { get; set; } = 10;
}
