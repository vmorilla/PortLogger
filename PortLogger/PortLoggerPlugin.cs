using System;
using System.Collections.Generic;
using System.Text;
using Plugin;


namespace PortLogger
{
    public class PortLoggerPlugin : iPlugin
    {
        private const int TARGET_PORT = 0x8080;

        private enum ReceiveState
        {
            ReceivingString,
            ReceivingParamSize,
            ReceivingParamData
        }

        private ReceiveState currentState = ReceiveState.ReceivingString;
        private List<byte> stringBuffer = new List<byte>();
        private List<object> parameters = new List<object>();
        private Queue<string> formatSpecifiers = new Queue<string>();

        private int expectedParamSize = 0;
        private List<byte> paramBuffer = new List<byte>();


        public List<sIO> Init(iCSpect c)
        {
            Console.WriteLine($"PortLogger plugin started on port 0x{TARGET_PORT:X4}");
            return new List<sIO> { new sIO(TARGET_PORT, eAccess.Port_Write) };
        }


        public void OSTick()
        {
        }

        public bool Write(eAccess type, int port, int id, byte value)
        {
            if (port != TARGET_PORT || type != eAccess.Port_Write)
                return false;

            switch (currentState)
            {
                case ReceiveState.ReceivingString:
                    if (value == 0)
                    {
                        ParseFormatSpecifiers();
                        if (formatSpecifiers.Count > 0)
                        {
                            currentState = ReceiveState.ReceivingParamSize;
                        }
                        else
                        {
                            FlushMessage();
                        }
                    }
                    else
                    {
                        stringBuffer.Add(value);
                    }
                    break;

                case ReceiveState.ReceivingParamSize:
                    expectedParamSize = value;
                    paramBuffer.Clear();
                    currentState = ReceiveState.ReceivingParamData;
                    break;

                case ReceiveState.ReceivingParamData:
                    paramBuffer.Add(value);
                    if (paramBuffer.Count >= expectedParamSize)
                    {
                        ProcessParameter();
                        if (parameters.Count >= formatSpecifiers.Count)
                        {
                            FlushMessage();
                        }
                        else
                        {
                            currentState = ReceiveState.ReceivingParamSize;
                        }
                    }
                    break;
            }

            return false;
        }

        public byte Read(eAccess _type, int _port, int _id, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }

        public bool KeyPressed(int _id)
        {
            return false;
        }


        private void ParseFormatSpecifiers()
        {
            string str = Encoding.ASCII.GetString(stringBuffer.ToArray());
            int i = 0;
            while (i < str.Length)
            {
                if (str[i] == '%')
                {
                    if (i + 1 < str.Length)
                    {
                        char spec = str[i + 1];
                        if (spec == 'i' || spec == 'u')
                        {
                            formatSpecifiers.Enqueue("%" + spec);
                            i++;
                        }
                    }
                }
                i++;
            }
        }

        private void ProcessParameter()
        {
            object param;
            if (expectedParamSize == 1)
            {
                param = (int)paramBuffer[0];
            }
            else if (expectedParamSize == 2)
            {
                param = (int)(paramBuffer[0] | (paramBuffer[1] << 8));
            }
            else if (expectedParamSize == 4)
            {
                param = DecodeMath32(paramBuffer.ToArray());
            }
            else
            {
                Console.WriteLine("Unsupported parameter size: " + expectedParamSize);
                param = 0;
            }

            parameters.Add(param);
        }

        private float DecodeMath32(byte[] bytes)
        {
            int mantissa = (bytes[0] << 16) | (bytes[1] << 8) | bytes[2];
            int exponent = bytes[3] - 0x80;

            if (mantissa == 0 && exponent == -0x80)
                return 0;

            float result = mantissa * (float)Math.Pow(2, exponent - 23);
            return result;
        }

        private void FlushMessage()
        {
            try
            {
                string formatStr = Encoding.ASCII.GetString(stringBuffer.ToArray());
                string formattedMessage = FormatMessage(formatStr, parameters);
                Console.WriteLine("[DebugOut] " + formattedMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DebugOut ERROR] " + ex.Message);
            }

            stringBuffer.Clear();
            parameters.Clear();
            formatSpecifiers.Clear();
            paramBuffer.Clear();
            expectedParamSize = 0;
            currentState = ReceiveState.ReceivingString;
        }

        private string FormatMessage(string format, List<object> args)
        {
            StringBuilder result = new StringBuilder();
            int argIndex = 0;
            for (int i = 0; i < format.Length; i++)
            {
                if (format[i] == '%' && i + 1 < format.Length)
                {
                    char spec = format[i + 1];
                    if ((spec == 'i' || spec == 'u') && argIndex < args.Count)
                    {
                        result.Append(args[argIndex++].ToString());
                        i++; // Skip format specifier
                    }
                    else
                    {
                        result.Append(format[i]);
                    }
                }
                else
                {
                    result.Append(format[i]);
                }
            }
            return result.ToString();
        }

        public void Tick() { }
        public void Quit() { }
        public byte Read(eAccess type, int address, out bool isValid) { isValid = false; return 0; }
        public void Reset() { }
    }
}
