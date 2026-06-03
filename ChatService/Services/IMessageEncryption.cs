namespace ChatService.Services;

public interface IMessageEncryption
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}