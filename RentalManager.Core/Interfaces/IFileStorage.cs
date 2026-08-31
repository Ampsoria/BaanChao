namespace RentalManager.Core.Interfaces;

public interface IFileStorage
{
    Task<StoredFile> SaveSlipAsync(Stream input, string contentType, DateTime receivedAtUtc, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}

public sealed record StoredFile(string RelativePath, string Sha256, string ContentType, long Length);
