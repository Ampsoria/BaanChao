using System.Globalization;
using System.Text;
using QRCoder;
using RentalManager.Core.Interfaces;

namespace RentalManager.Infrastructure.PromptPay;

public sealed class PromptPayService : IPromptPayService
{
    public string CreatePayload(string target, decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var normalized = new string(target.Where(char.IsDigit).ToArray());
        string proxyType;
        string proxy;
        if (normalized.Length == 10 && normalized.StartsWith('0'))
        {
            proxyType = "01";
            proxy = "0066" + normalized[1..];
        }
        else if (normalized.Length == 13)
        {
            proxyType = "02";
            proxy = normalized;
        }
        else
        {
            throw new ArgumentException("PromptPay target ต้องเป็นเบอร์มือถือ 10 หลักหรือเลขประจำตัว 13 หลัก", nameof(target));
        }

        var merchant = Tag("00", "A000000677010111") + Tag(proxyType, proxy);
        var payload = Tag("00", "01") + Tag("01", "12") + Tag("29", merchant) +
                      Tag("53", "764") + Tag("54", amount.ToString("0.00", CultureInfo.InvariantCulture)) +
                      Tag("58", "TH") + "6304";
        return payload + Crc16(payload);
    }

    public byte[] CreateQrPng(string target, decimal amount)
    {
        using var data = QRCodeGenerator.GenerateQrCode(CreatePayload(target, amount), QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(12);
    }

    private static string Tag(string id, string value) => $"{id}{value.Length:00}{value}";

    private static string Crc16(string value)
    {
        var crc = 0xFFFF;
        foreach (var valueByte in Encoding.ASCII.GetBytes(value))
        {
            crc ^= valueByte << 8;
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
            crc &= 0xFFFF;
        }
        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
