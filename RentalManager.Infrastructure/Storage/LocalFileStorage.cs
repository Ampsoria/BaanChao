using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RentalManager.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace RentalManager.Infrastructure.Storage;

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly string root = Path.GetFullPath(options.Value.SlipRoot);
    private readonly long maxBytes = options.Value.MaxUploadMegabytes * 1024L * 1024L;

    public async Task<StoredFile> SaveSlipAsync(
        Stream input, string contentType, DateTime receivedAtUtc, CancellationToken cancellationToken = default)
    {
        if (!new[] { "image/jpeg", "image/png", "image/webp" }.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("รองรับสลิปชนิด JPEG, PNG หรือ WebP เท่านั้น");

        await using var limited = new MemoryStream();
        await input.CopyToAsync(limited, cancellationToken);
        if (limited.Length is 0 || limited.Length > maxBytes)
            throw new InvalidDataException($"ไฟล์สลิปต้องมีขนาด 1 byte ถึง {maxBytes / 1024 / 1024} MB");
        limited.Position = 0;

        Image image;
        try
        {
            image = await Image.LoadAsync(limited, cancellationToken);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidDataException("เนื้อหาไฟล์ไม่ใช่รูปภาพที่รองรับ", exception);
        }
        using (image)
        {
            if (image.Width > 1200)
                image.Mutate(x => x.Resize(1200, 0));
            await using var encoded = new MemoryStream();
            await image.SaveAsJpegAsync(encoded, new JpegEncoder { Quality = 85 }, cancellationToken);
            var bytes = encoded.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var relativePath = Path.Combine(
                receivedAtUtc.Year.ToString("0000"), receivedAtUtc.Month.ToString("00"), $"{Guid.NewGuid():N}.jpg");
            var absolutePath = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);
            return new StoredFile(relativePath.Replace(Path.DirectorySeparatorChar, '/'), hash, "image/jpeg", bytes.Length);
        }
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(relativePath);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("เส้นทางไฟล์ไม่ถูกต้อง");
        return path;
    }
}
