using System.Globalization;
using RentalManager.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace RentalManager.Infrastructure.Slip;

public sealed class LocalSlipVerifier : ISlipVerifier
{
    public string Name => "Local";

    public async Task<SlipVerificationResult> VerifyAsync(
        Stream image, decimal expectedAmount, CancellationToken cancellationToken = default)
    {
        using var bitmap = await Image.LoadAsync<Rgba32>(image, cancellationToken);
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        bitmap.CopyPixelDataTo(pixels);
        var source = new RGBLuminanceSource(pixels, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGBA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
        };
        var result = reader.Decode(source);
        if (result is null)
            return new SlipVerificationResult(false, null, null, null, "ไม่พบ QR บนสลิป ต้องตรวจด้วยคน");

        var expected = expectedAmount.ToString("0.00", CultureInfo.InvariantCulture);
        var containsAmount = result.Text.Contains(expected, StringComparison.Ordinal);
        return new SlipVerificationResult(
            containsAmount,
            containsAmount ? expectedAmount : null,
            null,
            result.Text.Length <= 64 ? result.Text : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(result.Text))),
            containsAmount ? null : "อ่าน QR ได้แต่ยืนยันยอดไม่ได้ ต้องตรวจด้วยคน");
    }
}
