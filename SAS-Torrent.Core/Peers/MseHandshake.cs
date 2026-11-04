using System.Security.Cryptography;

namespace SAS_Torrent.Core.Peers;

public static class MseHandshake
{
    private static readonly byte[] DH_PRIME = new byte[] {
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xC9, 0x0F, 0xDA, 0xA2, 0x21, 0x68, 0xC2, 0x34,
        0xC4, 0xC6, 0x62, 0x8B, 0x80, 0xDC, 0x1C, 0xD1,
        0x29, 0x02, 0x4E, 0x08, 0x8A, 0x67, 0xCC, 0x74,
        0x02, 0x0B, 0xBE, 0xA6, 0x3B, 0x13, 0x9B, 0x22,
        0x51, 0x4A, 0x08, 0x79, 0x8E, 0x34, 0x04, 0xDD,
        0xEF, 0x95, 0x19, 0xB3, 0xCD, 0x3A, 0x43, 0x1B,
        0x30, 0x2B, 0x0A, 0x6D, 0xF2, 0x5F, 0x14, 0x37,
        0x4F, 0xE1, 0x35, 0x6D, 0x6D, 0x51, 0xC2, 0x45,
        0xE4, 0x85, 0xB5, 0x76, 0x62, 0x5E, 0x7E, 0xC6,
        0xF4, 0x4C, 0x42, 0xE9, 0xA6, 0x37, 0xED, 0x6B,
        0x0B, 0xFF, 0x5C, 0xB6, 0xF4, 0x06, 0xB7, 0xED,
        0xEE, 0x38, 0x6B, 0xFB, 0x5A, 0x89, 0x9F, 0xA5,
        0xAE, 0x9F, 0x24, 0x11, 0x7C, 0x4B, 0x1F, 0xE6,
        0x49, 0x28, 0x66, 0x51, 0xEC, 0xE6, 0x53, 0x81,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
    };

    private const int DH_GENERATOR = 2;
    private const int DH_KEY_SIZE = 96;

    public static (byte[] publicKey, byte[] privateKey) GenerateKeyPair()
    {
        using var rng = RandomNumberGenerator.Create();
        var privateKey = new byte[DH_KEY_SIZE];
        rng.GetBytes(privateKey);
        privateKey[0] &= 0x7F;
        var publicKey = ModExp(new System.Numerics.BigInteger(DH_GENERATOR), privateKey, DH_PRIME);
        return (publicKey, privateKey);
    }

    public static byte[] ComputeSharedSecret(byte[] theirPublicKey, byte[] myPrivateKey)
    {
        return ModExp(BigEndianToInt(theirPublicKey), myPrivateKey, DH_PRIME);
    }

    public static byte[] DeriveKey(byte[] sharedSecret, byte[] skey, byte[] prefix)
    {
        using var sha1 = SHA1.Create();
        var input = new byte[sharedSecret.Length + skey.Length + prefix.Length];
        Buffer.BlockCopy(sharedSecret, 0, input, 0, sharedSecret.Length);
        Buffer.BlockCopy(skey, 0, input, sharedSecret.Length, skey.Length);
        Buffer.BlockCopy(prefix, 0, input, sharedSecret.Length + skey.Length, prefix.Length);
        return sha1.ComputeHash(input);
    }

    public static byte[] ModExp(System.Numerics.BigInteger generator, byte[] exponent, byte[] modulus)
    {
        var g = new byte[modulus.Length];
        g[g.Length - 1] = (byte)generator;
        var result = new byte[modulus.Length];
        result[result.Length - 1] = 1;
        var baseBytes = g;

        for (int i = 0; i < exponent.Length; i++)
        {
            byte expByte = exponent[i];
            for (int bit = 7; bit >= 0; bit--)
            {
                result = ModMul(result, result, modulus);
                if ((expByte & (1 << bit)) != 0)
                    result = ModMul(result, baseBytes, modulus);
            }
        }
        return PadToLength(result, modulus.Length);
    }

    private static byte[] ModMul(byte[] a, byte[] b, byte[] modulus)
    {
        var aInt = BigEndianToInt(a);
        var bInt = BigEndianToInt(b);
        var mInt = BigEndianToInt(modulus);

        var result = BigIntegerMultiplyMod(aInt, bInt, mInt);
        return IntToBigEndian(result, modulus.Length);
    }

    private static System.Numerics.BigInteger BigEndianToInt(byte[] bytes)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new System.Numerics.BigInteger(bytes.Concat(new byte[] { 0 }).ToArray());
    }

    private static byte[] IntToBigEndian(System.Numerics.BigInteger value, int length)
    {
        var bytes = value.ToByteArray();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        if (bytes.Length > length && bytes[0] == 0)
            bytes = bytes[1..];
        if (bytes.Length < length)
        {
            var padded = new byte[length];
            Buffer.BlockCopy(bytes, 0, padded, length - bytes.Length, bytes.Length);
            return padded;
        }
        return bytes[^length..];
    }

    private static System.Numerics.BigInteger BigIntegerMultiplyMod(System.Numerics.BigInteger a, System.Numerics.BigInteger b, System.Numerics.BigInteger m)
    {
        return (a * b) % m;
    }

    private static byte[] PadToLength(byte[] data, int length)
    {
        if (data.Length >= length) return data[^length..];
        var padded = new byte[length];
        Buffer.BlockCopy(data, 0, padded, length - data.Length, data.Length);
        return padded;
    }
}

public sealed class Rc4Cipher
{
    private readonly byte[] _s = new byte[256];
    private int _i, _j;

    public Rc4Cipher(byte[] key)
    {
        for (int i = 0; i < 256; i++) _s[i] = (byte)i;
        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + _s[i] + key[i % key.Length]) % 256;
            (_s[i], _s[j]) = (_s[j], _s[i]);
        }
    }

    public void Encrypt(byte[] data, int offset, int count)
    {
        for (int k = 0; k < count; k++)
        {
            _i = (_i + 1) % 256;
            _j = (_j + _s[_i]) % 256;
            (_s[_i], _s[_j]) = (_s[_j], _s[_i]);
            byte keystream = _s[(_s[_i] + _s[_j]) % 256];
            data[offset + k] ^= keystream;
        }
    }

    public void Decrypt(byte[] data, int offset, int count) => Encrypt(data, offset, count);
}
