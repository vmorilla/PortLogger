using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Xml.Schema;
using Plugin;

namespace PortLogger
{
    public class PortLoggerPlugin : iPlugin
    {
        private int debugPort = 0x8080;
        private iCSpect cspect;

        private Z80Logger z80Logger;

        public List<sIO> Init(iCSpect c)
        {
            cspect = c;
            z80Logger = new Z80Logger(cspect);

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
                Log(z80Logger.Log(cspect.GetRegs().IX));
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