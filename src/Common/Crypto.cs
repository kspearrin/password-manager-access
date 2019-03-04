// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System.IO;
using System.Security.Cryptography;

namespace PasswordManagerAccess.Common
{
    internal static class Crypto
    {
        //
        // SHA
        //

        public static byte[] Sha256(string message)
        {
            return Sha256(message.ToBytes());
        }

        public static byte[] Sha256(byte[] message)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(message);
        }

        //
        // PBKDF2
        //

        public static byte[] Pbkdf2Sha1(string password, byte[] salt, int iterations, int byteCount)
        {
            return Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA1, byteCount);
        }

        public static byte[] Pbkdf2Sha256(string password, byte[] salt, int iterations, int byteCount)
        {
            return Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, byteCount);
        }

        public static byte[] Pbkdf2Sha512(string password, byte[] salt, int iterations, int byteCount)
        {
            return Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, byteCount);
        }

        //
        // AES
        //

        public static byte[] DecryptAes256Cbc(byte[] ciphertext, byte[] iv, byte[] key)
        {
            return DecryptAes256Cbc(ciphertext, iv, key, PaddingMode.PKCS7);
        }

        public static byte[] DecryptAes256CbcNoPadding(byte[] ciphertext, byte[] iv, byte[] key)
        {
            return DecryptAes256Cbc(ciphertext, iv, key, PaddingMode.None);
        }

        //
        // Private
        //

        private static byte[] Pbkdf2(string password,
                                     byte[] salt,
                                     int iterations,
                                     HashAlgorithmName hash,
                                     int byteCount)
        {
            using (var db = new Rfc2898DeriveBytes(password, salt, iterations, hash))
                return db.GetBytes(byteCount);
        }

        private static byte[] DecryptAes256Cbc(byte[] ciphertext, byte[] iv, byte[] key, PaddingMode padding)
        {
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Key = key;
                    aes.Mode = CipherMode.CBC;
                    aes.IV = iv;
                    aes.Padding = padding;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var cryptoStream = new CryptoStream(new MemoryStream(ciphertext, false),
                                                               decryptor,
                                                               CryptoStreamMode.Read))
                    using (var outputStream = new MemoryStream())
                    {
                        cryptoStream.CopyTo(outputStream);
                        return outputStream.ToArray();
                    }
                }
            }
            catch (CryptographicException e)
            {
                throw new CryptoException("AES decryption failed", e);
            }
        }
    }
}
