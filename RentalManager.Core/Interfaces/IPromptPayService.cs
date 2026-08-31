namespace RentalManager.Core.Interfaces;

public interface IPromptPayService
{
    string CreatePayload(string target, decimal amount);
    byte[] CreateQrPng(string target, decimal amount);
}
