using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Plugin;

namespace PortLogger
{
    public class PortLoggerPlugin : iPlugin
    {
        private int debugPort = 0x8080;
        private iCSpect cspect;

        private enum ReceiveState
        {
            ReceivingString,
            ReceivingParamData
        }

        private ReceiveState currentState = ReceiveState.ReceivingString;
        private List<byte> stringBuffer = new List<byte>();
        private List<char> parameters = new List<char>();
        private int currentParameterIndex = 0;
        private int currentParameterLength = 0;
        private List<string> parametersValue = new List<string>();

        private List<byte> paramBuffer = new List<byte>();

        public List<sIO> Init(iCSpect c)
        {
            cspect = c;
            var sIOs = new List<sIO> { new sIO(debugPort, eAccess.Port_Write) };
            LoadConfig();
            Log($"Plugin started on port 0x{debugPort:X4}");

            return sIOs;
        }


        public void OSTick()
        {
        }

        public bool Write(eAccess type, int port, int id, byte value)
        {
            if (port == debugPort && type == eAccess.Port_Write)
            {
                WriteLogPort(value);
                return true;
            }

            return false;
        }


        public byte Read(eAccess type, int port, int _id, out bool isvalid)
        {
            isvalid = false;
            return 0;
        }


        public bool KeyPressed(int _id)
        {
            return true;
        }


        private void WriteLogPort(byte value)
        {
            switch (currentState)
            {
                case ReceiveState.ReceivingString:
                    if (value == 0)
                    {
                        ParseFormatSpecifiers();
                        if (parameters.Count > 0)
                        {
                            PrepareParameter();
                            currentState = ReceiveState.ReceivingParamData;
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

                case ReceiveState.ReceivingParamData:
                    paramBuffer.Add(value);
                    if (--currentParameterLength == 0)
                    {
                        ParseCurrentParameter();
                    }
                    break;
            }
        }

        private void LoadConfig()
        {
            string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PortLogger.cfg");

            if (!System.IO.File.Exists(configPath))
            {
                Log("No config file found, using defaults.");
                return;
            }

            foreach (var line in System.IO.File.ReadLines(configPath))
            {
                if (line.StartsWith("DebugPort="))
                {
                    int port = ParseIntWithHexSupport(line.Substring("DebugPort=".Length));
                    if (port >= 0)
                    {
                        debugPort = port;
                    }

                }
            }
        }


        private void PrepareParameter()
        {
            var currentParameter = parameters[this.currentParameterIndex];
            this.paramBuffer.Clear();

            switch (currentParameter)
            {
                case 'c':
                    this.currentParameterLength = 1;
                    break;
                case 'f':
                    this.currentParameterLength = 4;
                    break;
                default:
                    this.currentParameterLength = 2;
                    break;
            }
        }

        private void ParseCurrentParameter()
        {
            var currentParameter = parameters[this.currentParameterIndex];
            string value;

            switch (currentParameter)
            {
                case 'c':
                    value = Encoding.ASCII.GetString(paramBuffer.ToArray());
                    break;
                case 'f':
                    value = "Not imp";
                    break;
                default:
                    int numValue = paramBuffer[0] + paramBuffer[1] * 256;
                    value = numValue.ToString();
                    break;
            }

            this.parametersValue.Add(value);
            this.currentParameterIndex++;
            if (this.currentParameterIndex >= this.parameters.Count)
            {
                FlushMessage();
            }
            else
            {
                PrepareParameter();
            }

        }


        private static int ParseIntWithHexSupport(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return -1;

            // Trim whitespace
            input = input.Trim();

            if (string.IsNullOrWhiteSpace(input))
                return -1;

            // Handle $-prefixed hex (assembler style)
            if (input.StartsWith("$"))
            {
                return int.Parse(input.Substring(1), NumberStyles.HexNumber);
            }

            // Handle 0x-prefixed hex (C-style)
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.Parse(input.Substring(2), NumberStyles.HexNumber);
            }

            int number;
            // Try to parse as decimal
            if (int.TryParse(input, out number))
            {
                return number;
            }

            return -1;
        }


        private void ParseFormatSpecifiers()
        {
            this.parameters = new List<char> { };
            this.currentParameterIndex = 0;
            this.parametersValue = new List<String> { };

            string str = Encoding.ASCII.GetString(stringBuffer.ToArray());
            int i = 0;
            while (i < str.Length)
            {
                if (str[i] == '%')
                {
                    if (i + 1 < str.Length)
                    {
                        char spec = str[i + 1];
                        if (spec != '%') { parameters.Add(spec); }
                    }
                }
                i++;
            }
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
                string formattedMessage = FormatMessage(formatStr, parametersValue);
                Log(formattedMessage);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }

            stringBuffer.Clear();
            parameters.Clear();
            currentState = ReceiveState.ReceivingString;
        }

        private string FormatMessage(string format, List<String> args)
        {
            StringBuilder result = new StringBuilder();
            int argIndex = 0;
            for (int i = 0; i < format.Length; i++)
            {
                if (format[i] == '%' && i + 1 < format.Length)
                {
                    char spec = format[++i];
                    if (spec == '%')
                        result.Append('%');
                    else
                    {
                        result.Append(parametersValue[argIndex++]);
                    }
                }
                else
                {
                    result.Append(format[i]);
                }
            }
            return result.ToString();
        }

        private void Log(string message)
        {
            Console.WriteLine("[PortLogger] " + message);
        }

        public void Tick() { }
        public void Quit() { }
        public byte Read(eAccess type, int address, out bool isValid) { isValid = false; return 0; }
        public void Reset() { }
    }
}