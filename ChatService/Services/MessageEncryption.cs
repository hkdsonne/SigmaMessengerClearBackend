using System.Security.Cryptography;
using System.Text;

namespace ChatService.Services;

public class MessageEncryption : IMessageEncryption
{
    private readonly byte[] _key;

    public MessageEncryption(string keyBase64)
    {
        var keyBytes = Convert.FromBase64String(keyBase64);
        if (keyBytes.Length != 32)
            throw new ArgumentException("Key must be 32 bytes (AES-256)");
        _key = keyBytes;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length);
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return cipherText;

        byte[] fullCipher;

        try
        {
            fullCipher = Convert.FromBase64String(cipherText);
        }
        catch
        {
            return cipherText;
        }

        if (fullCipher.Length <= 16)
            return cipherText;

        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;

            var iv = new byte[16];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, 16, fullCipher.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch
        {
            return cipherText;
        }
    }
}