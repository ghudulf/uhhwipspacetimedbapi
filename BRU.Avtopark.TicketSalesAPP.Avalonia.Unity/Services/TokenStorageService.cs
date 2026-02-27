using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace BRU.Avtopark.TicketSalesAPP.Avalonia.Unity.Services
{
    public class TokenStorageService
    {
        private readonly string _tokenFilePath;
        private readonly byte[] _encryptionKey;

        public TokenStorageService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "BRU.Avtopark.TicketSalesApp");
            Directory.CreateDirectory(appFolder);
            _tokenFilePath = Path.Combine(appFolder, "tokens.dat");

            // Generate or retrieve encryption key (in production, use a more secure method)
            _encryptionKey = GetOrCreateEncryptionKey(appFolder);
        }

        public async Task SaveTokensAsync(OAuthTokenResponse tokens)
        {
            try
            {
                // Calculate expiration time
                tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);

                var json = JsonSerializer.Serialize(tokens);
                var encrypted = EncryptString(json);
                await File.WriteAllBytesAsync(_tokenFilePath, encrypted);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tokens: {ex.Message}");
                throw;
            }
        }

        public async Task<OAuthTokenResponse?> GetTokensAsync()
        {
            try
            {
                if (!File.Exists(_tokenFilePath))
                {
                    return null;
                }

                var encrypted = await File.ReadAllBytesAsync(_tokenFilePath);
                var json = DecryptString(encrypted);
                return JsonSerializer.Deserialize<OAuthTokenResponse>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading tokens: {ex.Message}");
                return null;
            }
        }

        public async Task ClearTokensAsync()
        {
            try
            {
                if (File.Exists(_tokenFilePath))
                {
                    await Task.Run(() => File.Delete(_tokenFilePath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing tokens: {ex.Message}");
            }
        }

        private byte[] GetOrCreateEncryptionKey(string appFolder)
        {
            var keyPath = Path.Combine(appFolder, ".key");
            
            if (File.Exists(keyPath))
            {
                return File.ReadAllBytes(keyPath);
            }

            // Generate new key
            using var aes = Aes.Create();
            aes.GenerateKey();
            var key = aes.Key;
            
            // Save key with restricted permissions
            File.WriteAllBytes(keyPath, key);
            
            return key;
        }

        private byte[] EncryptString(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            
            // Write IV first
            ms.Write(aes.IV, 0, aes.IV.Length);
            
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return ms.ToArray();
        }

        private string DecryptString(byte[] cipherText)
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;

            // Read IV from the beginning
            var iv = new byte[aes.IV.Length];
            Array.Copy(cipherText, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipherText, iv.Length, cipherText.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            
            return sr.ReadToEnd();
        }
    }
}
