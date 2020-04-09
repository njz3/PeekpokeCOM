using PeekpokeCOM.Utils;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeekpokeCOM.Shells
{
    public class SerialShell : Shell
    {
        protected SerialPort COMTerminal = null;

        public SerialShell(string portName, int baudrate) : base()
        {
            COMTerminal = new SerialPort(portName, baudrate);
            COMTerminal.NewLine = "\n";
            COMTerminal.DtrEnable = true;
            COMTerminal.RtsEnable = true;

            // High timeout for handshaking and basic IO
            COMTerminal.ReadTimeout =-1;
            COMTerminal.WriteTimeout = 10000;
            COMTerminal.ReceivedBytesThreshold = 100;
        }


        public override bool Open()
        {
            if (this.IsOpened)
                return true;
            Log("Opening port " + COMTerminal.PortName + " at " + COMTerminal.BaudRate);
            try {
                COMTerminal.Open();
                this.IsOpened = true;
                return true;
            } catch(Exception ex) {
                Log("Error openning port " + COMTerminal.PortName + " with " + ex.Message);
                this.IsOpened = false;
                return false;
            }
        }

        public override void Close()
        {
            if (!this.IsOpened)
                return;
            Log("Close port " + COMTerminal.PortName);
            COMTerminal.Close();
            this.IsOpened = false;
        }


        public override bool ProcessOneMessage()
        {
            if (COMTerminal.BytesToRead < 2)
                return false;
            // Parse message from IO board
            List<string> outmsg = new List<string>();
            try {
                var mesg = COMTerminal.ReadLine();
                Log("Recv<<" + mesg);
                var stt = ParseAndExecuteOneLine(mesg, outmsg);
                SendOutput(String.Format("0x{0:X}", (int)stt));
                foreach (var st in outmsg) {
                    SendOutput(st);
                }
            } catch (Exception ex) {
                Log("ProcessOneMessage got exception " + ex.Message, LogLevels.DEBUG);
            }
            return false;
        }

        public override bool SendOutput(string text)
        {
            Log("Sent>>" + text);
            COMTerminal.WriteLine(text);
            return true;
        }
    }
}
