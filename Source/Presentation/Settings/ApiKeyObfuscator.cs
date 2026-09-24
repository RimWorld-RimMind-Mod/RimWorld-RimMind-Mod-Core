using System;
using RimMind.Domain.Enums;

namespace RimMind.Presentation.Settings
{
    public static class ApiKeyObfuscator
    {
        private const byte XorKey = 0x5A;

        public static string Obfuscate(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(key);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ XorKey);
            return Convert.ToBase64String(bytes);
        }

        public static string Deobfuscate(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            try
            {
                var bytes = Convert.FromBase64String(key);
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = (byte)(bytes[i] ^ XorKey);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
