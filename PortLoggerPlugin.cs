using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Plugin;

// x

namespace PortLogger
{
    public class PortLoggerPlugin : iPlugin
    {
        private int debugPort = 0x8080;
        private bool startWatching = false;
        private List<int> watchAddresses = new List<int>();
        private iCSpect cspect;

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
            cspect = c;
            var sIOs = new List<sIO> { new sIO(debugPort, eAccess.Port_Write) };
            LoadConfig();
            Log($"Plugin started on port 0x{debugPort:X4}");
            foreach (var address in watchAddresses)
            {
                sIOs.Add(new sIO(address, eAccess.Memory_Write));
                Log($"Watching address 0x{address:X4}");
            }

            sIOs.Add(new sIO(0xFF3E, eAccess.Memory_Read));

            // Add key press 
            sIOs.Add(new sIO("<ctrl>g", eAccess.KeyPress, 0));
            sIOs.Add(new sIO("<ctrl>h", eAccess.KeyPress, 1));

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
            else if (watchAddresses.Contains(port) && type == eAccess.Memory_Write && startWatching)
            {
                Log($"Memory write attempt to {port}... Halting");
                cspect.Debugger(eDebugCommand.Enter);
                return true;
            }
            //Log($"Other write attempts {port}");
            return false;
        }


        public byte Read(eAccess type, int port, int _id, out bool isvalid)
        {
            if (type == eAccess.Memory_Read)
            {
                var pc = cspect.GetRegs().PC;
                if (pc >= 0x2F7C && pc <= 0x2FD5)
                {
                    var byte0 = cspect.Peek(0); //0xFF3E);
                    var byte1 = cspect.Peek(1); //0xFF3F);
                    // B6FF or B6EB
                    if (byte1 == 0xB6 && (byte0 == 0xFF || byte0 == 0xEB))
                    {
                        Log("Good...");
                    }
                    else
                    {
                        Log($"Unexpected value in 0xFF3E: {byte1 * 256 + byte0:X}");
                        cspect.Debugger(eDebugCommand.Enter);
                    }

                }
            }
            isvalid = false;
            return 0;
        }


        public bool KeyPressed(int _id)
        {
            var enabled = _id == 1 ? "enabled" : "disabled";
            Log($"MemWatch {enabled}.");
            startWatching = _id == 1;
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
                        Log($"Loaded config: DebugPort=0x{debugPort:X}");
                    }

                }
                else if (line.StartsWith("WatchAddress="))
                {
                    var numberStrs = line.Substring("WatchAddress=".Length).Split(',');

                    // Parse the numbers based on their format
                    int address = ParseIntWithHexSupport(numberStrs[0]);
                    int length = numberStrs.Length > 1 ? ParseIntWithHexSupport(numberStrs[1]) : 1;

                    Log($"Loaded config: WatchAddress={address:X},{length}");

                    for (int i = 0; i < length; i++)
                    {
                        watchAddresses.Add(address + i);
                    }
                }
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
                Log("Unsupported parameter size: " + expectedParamSize);
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
                Log(formattedMessage);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
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
