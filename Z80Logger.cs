using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Plugin;

public class Z80Logger
{
    private iCSpect cspect;
    private ushort address;

    public Z80Logger(iCSpect cspect)
    {
        this.cspect = cspect;
    }

    public string Log(ushort address)
    {
        this.address = address;
        string rawMessage = PeekString(PeekWord());

        StringBuilder result = new StringBuilder();
        int length = rawMessage.Length;
        for (int i = 0; i < length; i++)
        {
            char c = rawMessage[i];
            if (c == '%' && i + 1 < length)
            {
                // Start of format specifier
                int specifierStart = i + 1;
                int specifierEnd = specifierStart;

                // Find the end of the format specifier
                while (specifierEnd < length && !char.IsLetter(rawMessage[specifierEnd]))
                {
                    specifierEnd++;
                }

                if (specifierEnd < length)
                {
                    // Include the letter in the specifier
                    specifierEnd++;

                    string formatSpec = rawMessage.Substring(specifierStart, specifierEnd - specifierStart);
                    result.Append(FormatArgument(formatSpec));
                    i = specifierEnd - 1; // Move index to end of specifier
                }
                else
                {
                    // Malformed specifier, just append '%'
                    result.Append(c);
                }
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();

    }

    private ushort PeekWord()
    {
        byte[] bytes = cspect.Peek(address, 2);
        address += 2;
        return (ushort)(bytes[0] + 256 * bytes[1]);
    }

    private string PeekString(ushort address)
    {
        var bytes = new List<byte>();
        ushort currentAddress = address;
        byte b;

        while ((b = cspect.Peek(currentAddress)) != 0)
        {
            bytes.Add(b);
            currentAddress++;
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }


    /// <summary>
    /// Formats a little-endian byte array according to a printf-style format specifier.
    /// </summary>
    /// <param name="formatSpec">Format specifier (e.g., "d", "x", "04X", "u")</param>
    /// <returns>Formatted string</returns>
    private string FormatArgument(string formatSpec)
    {
        // Parse the format specifier
        char specifier = formatSpec[formatSpec.Length - 1];
        string modifiers = formatSpec.Substring(0, formatSpec.Length - 1);

        // Format based on specifier
        string result;
        switch (specifier)
        {
            case 'c':
                result = ((char)PeekWord()).ToString();
                break;
            case 'd':
            case 'i':
                // Signed integer
                long signedValue = ConvertToSigned(PeekWord(), 2);
                result = signedValue.ToString();
                break;

            case 'u':
                // Unsigned integer
                result = PeekWord().ToString();
                break;

            case 'x':
            case 'X':
                result = FormatHex(PeekWord(), specifier, modifiers);
                break;

            case 'o':
                // Octal
                result = Convert.ToString(PeekWord(), 8);
                break;

            case 'f':
            case 'F':
                // Float (interpret bytes as integer then convert)
                result = DecodeMath32(cspect.Peek(address, 4)).ToString(CultureInfo.InvariantCulture);
                address += 4;
                break;

            case 's':
                // String
                result = PeekString(PeekWord());
                break;

            default:
                result = $"%{formatSpec}";
                break;
        }

        return result;
    }

    private static long ConvertToSigned(ulong unsignedValue, int byteCount)
    {
        // Sign-extend based on the most significant bit
        int bits = byteCount * 8;
        long mask = (1L << bits) - 1;
        long value = (long)(unsignedValue & (ulong)mask);

        // Check if sign bit is set
        long signBit = 1L << (bits - 1);
        if ((value & signBit) != 0)
        {
            // Negative number - sign extend
            value |= ~mask;
        }

        return value;
    }

    private static int ExtractWidth(string modifiers)
    {
        int width = 0;
        foreach (char c in modifiers)
        {
            if (char.IsDigit(c))
                width = width * 10 + (c - '0');
        }
        return width;
    }

    private static float DecodeMath32(byte[] bytes)
    {
        uint bits = BitConverter.ToUInt32(bytes, 0);
        // Handle zero case
        if (bits == 0)
            return 0.0f;
        // Extract sign, exponent, and mantissa
        int sign = (bits & 0x80000000) != 0 ? -1 : 1;
        int exponent = (int)((bits >> 23) & 0xff) - 0x7f;
        double mantissa = 1;
        for (int i = 22; i >= 0; i--)
        {
            uint bitMask = 1u << i;
            if ((bits & bitMask) != 0)
            {
                int power = -(23 - i); // Negative power
                mantissa += Math.Pow(2, power);
            }
        }

        double result = sign * mantissa * Math.Pow(2, exponent);
        return (float)result;
    }

    private static string FormatHex(ushort value, char specifier, string modifiers)
    {
        if (!string.IsNullOrEmpty(modifiers) && modifiers.Contains("0"))
        {
            int width = ExtractWidth(modifiers);
            return value.ToString(specifier + width.ToString());
        }
        else
        {
            return value.ToString(specifier.ToString());
        }
    }
}
