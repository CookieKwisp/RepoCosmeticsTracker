using System;
using System.IO;
using System.Security.Cryptography;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Implements the AES-128-CBC scheme Unity's "Easy Save 3" asset uses,
    /// which R.E.P.O. uses for its .es3 save files.
    ///
    /// Algorithm (verified against a known-working reference implementation,
    /// not guessed): the first 16 bytes of the file are a random salt that
    /// doubles as the AES IV. The key is PBKDF2-HMAC-SHA1(password, salt,
    /// 100 iterations, 16 bytes). The remaining bytes are AES-128-CBC
    /// ciphertext with PKCS7 padding.
    /// </summary>
    public static class Es3Crypto
    {
        // The password R.E.P.O. uses for its ES3 encryption settings.
        // It's compiled into the game's assembly and has been documented
        // publicly by the save-editing community (e.g. N0edL/R.E.P.O-Save-Editor).
        public const string RepoPassword = "Why would you want to cheat?... :o It's no fun. :') :'D";

        public static byte[] Decrypt(byte[] fileBytes, string password)
        {
            if (fileBytes.Length < 16)
                throw new InvalidDataException("File is too short to contain an ES3 salt/IV header.");

            byte[] salt = fileBytes[..16];
            byte[] cipherText = fileBytes[16..];
            byte[] key = DeriveKey(password, salt);

            using Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.Key = key;
            aes.IV = salt;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        }

        public static byte[] Encrypt(byte[] plainBytes, string password, byte[] salt)
        {
            if (salt.Length != 16)
                throw new ArgumentException("Salt/IV must be 16 bytes.", nameof(salt));

            byte[] key = DeriveKey(password, salt);

            using Aes aes = Aes.Create();
            aes.KeySize = 128;
            aes.Key = key;
            aes.IV = salt;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            byte[] result = new byte[salt.Length + cipherText.Length];
            Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
            Buffer.BlockCopy(cipherText, 0, result, salt.Length, cipherText.Length);
            return result;
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100, HashAlgorithmName.SHA1);
            return pbkdf2.GetBytes(16);
        }
    }
}
