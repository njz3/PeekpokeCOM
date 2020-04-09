using PeekpokeCOM.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeekpokeCOM.Shells
{
    public class Shell
    {
        public enum StatusCode : int {
            Success = 0,
            ETooFewArguments,
            EInvalidSyntax,
            EUnknownCommand,
            EInvalidParameter,
            ECannotOpenProcess,
            ENoOpenProcess,
            EModuleNotFound,
            EWrongAddress,
        }
        
        char[] separators = new char[] { ' ', '\t', '\n', '\r' };

        public UInt64 ConvertHexToUInt64(string hexvalue)
        {
            UInt64.TryParse(hexvalue, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value);
            return value;
        }
        public byte ConvertHexToByte(string hexvalue)
        {
            byte.TryParse(hexvalue, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value);
            return value;
        }
        public string ConvertByteToHex(byte value)
        {
            return String.Format("0:X2", value);
        }

        public bool ParseInt64NumberWithHex(string value, out Int64 int64 )
        {
            var numstr = value.Replace("+", "");
            if (numstr.StartsWith("0x")) {
                // Hex string
                int64 = (Int64)ConvertHexToUInt64(numstr.Substring(2));
                return true;
            } else {
                // Decimal string with leading + or -
                if (!Int64.TryParse(numstr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int64))
                    return false;
                return true;
            }
        }
        public bool ParseUInt64NumberWithHex(string value, out UInt64 uint64)
        {
            var numstr = value.Replace("+", "");
            if (numstr.StartsWith("0x")) {
                // Hex string
                uint64 = (UInt64)ConvertHexToUInt64(numstr.Substring(2));
                return true;
            } else {
                // Decimal string with leading + or -
                if (!UInt64.TryParse(numstr, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint64))
                    return false;
                return true;
            }
        }


        public delegate StatusCode Handler(List<string> inargs, List<string> outmsg);

        public StatusCode CmdHelp(List<string> inargs, List<string> outmsg)
        {
            outmsg.Add("help");
            outmsg.Add("open");
            outmsg.Add("close");
            outmsg.Add("list");
            outmsg.Add("peek");
            outmsg.Add("poke");
            return StatusCode.Success;
        }

        ProcessManipulation ProcRW;

        public StatusCode CmdOpenProcess(List<string> inargs, List<string> outmsg)
        {
            if (inargs.Count<2)
                return StatusCode.ETooFewArguments;
            var procname = inargs[1];
            procname = procname.Replace("\"", "");
            if (ProcRW!=null) {
                ProcRW.CloseProcess();
                ProcRW = null;
            }
            ProcRW = new ProcessManipulation();
            if (ProcRW.OpenProcessForReadWrite(procname)) {
                return StatusCode.Success;
            } else {
                return StatusCode.ECannotOpenProcess;
            }
        }
        public StatusCode CmdListModules(List<string> inargs, List<string> outmsg)
        {
            if (inargs.Count>1)
                return StatusCode.EInvalidSyntax;
            if (ProcRW==null) {
                return StatusCode.ENoOpenProcess;
            }
            if (ProcRW.GetListOfModules(outmsg)) {
                return StatusCode.Success;
            } else {
                return StatusCode.ECannotOpenProcess;
            }
        }
        public StatusCode CmdCloseProcess(List<string> inargs, List<string> outmsg)
        {
            ProcRW.CloseProcess();
            ProcRW = null;
            return StatusCode.Success;
        }


        char[] OffsetsSplitters = new char[] { '[', ']', ',', ' ' };
        char[] ModuleSplitters = new char[] { '(', ')', ' ' };
        public StatusCode DecodeAddress(string address, out IntPtr ptr)
        {
            // An address must be expressed as:
            // (module)[+-num,+-num,+-num,+-FF]
            // "module" is optionnal

            // Start with base address
            ptr = ProcRW.BaseAddress;

            // Base address is absolute to process, or relative to a module ?
            var parentstart = address.IndexOf('(');
            if (parentstart>=0) {
                var parentend = address.IndexOf(')');
                if (parentend<parentstart) {
                    return StatusCode.EInvalidSyntax;
                }
                var modulestr = address.Substring(parentstart+1, parentend-parentstart-1);
                // This is a module relative address
                var modulename = modulestr.Split(ModuleSplitters, StringSplitOptions.RemoveEmptyEntries);
                if (modulename.Length==1) {
                    ProcRW.GetModuleBaseAddress(out var moduleaddr, modulename[0]);
                    ptr = moduleaddr;
                } else {
                    return StatusCode.EModuleNotFound;
                }
                // Remove () from address
                address = address.Substring(parentend+1);
            }

            // Ensure syntax
            var bracketstart = address.IndexOf('[');
            if (bracketstart<0) {
                return StatusCode.EInvalidSyntax;
            }
            var bracketend = address.IndexOf(']');
            if (bracketend<bracketstart) {
                return StatusCode.EInvalidSyntax;
            }
            List<int> offsets = new List<int>();
            var tokens = address.Split(OffsetsSplitters, StringSplitOptions.RemoveEmptyEntries);
            for(int i=0; i<tokens.Length; i++) {
                // Parse number
                var numstr = tokens[i].Replace("+","");
                if (numstr.StartsWith("0x")) {
                    // Hex string
                    var offset = (int)ConvertHexToUInt64(numstr.Substring(2));
                    offsets.Add(offset);
                } else {
                    // Decimal string with leading + or -
                    if (!int.TryParse(numstr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
                        return StatusCode.EInvalidSyntax;
                    offsets.Add(offset);
                }
            }
            // No offset?
            if (offsets.Count==0) {
                return StatusCode.Success;
            } else if (offsets.Count==1) {
                // Single offset = simple offset on module
                ptr += offsets[0];
                return StatusCode.Success;
            } else {
                // 'n' multiple offset, meaning the n-1 are pointers, and
                // last one is an offset
                if (!ProcRW.FindDmaAddress(ptr, offsets.GetRange(0, offsets.Count-1), out var pointer))
                    return StatusCode.EWrongAddress;
                ptr = pointer + offsets.Last();
            }
            return StatusCode.Success;
        }

        public StatusCode CmdPeek(List<string> inargs, List<string> outmsg)
        {
            if (ProcRW==null)
                return StatusCode.ENoOpenProcess;
            if (inargs.Count<2)
                return StatusCode.ETooFewArguments;
            var stt = DecodeAddress(inargs[1], out var address);
            if (stt!= StatusCode.Success) return stt;
            if (ProcRW.ReadByte((ulong)address, out var value)) {
                outmsg.Add(String.Format("0x{0:X}", value));
                return StatusCode.Success;
            }
            return StatusCode.EWrongAddress;
        }
        public StatusCode CmdPoke(List<string> inargs, List<string> outmsg)
        {
            if (ProcRW==null)
                return StatusCode.ENoOpenProcess;
            if (inargs.Count<3)
                return StatusCode.ETooFewArguments;
            var stt = DecodeAddress(inargs[1], out var address);
            if (stt!= StatusCode.Success) return stt;
            ParseUInt64NumberWithHex(inargs[2], out var val);
            if (ProcRW.WriteByte((ulong)address, (byte)(val&0xFF))) {
                return StatusCode.Success;
            }
            return StatusCode.EWrongAddress;
        }


        Dictionary<string, Handler> Handlers = new Dictionary<string, Handler>();

        public Shell()
        {
            Handlers.Add("help", CmdHelp);
            Handlers.Add("open", CmdOpenProcess);
            Handlers.Add("close", CmdOpenProcess);
            Handlers.Add("list", CmdListModules);
            Handlers.Add("peek", CmdPeek);
            Handlers.Add("poke", CmdPoke);
        }

        public void Log(string text, LogLevels level = LogLevels.DEBUG)
        {
            Logger.Log(text, level);
        }
        protected bool IsOpened = false;
        public virtual bool Open()
        { 
            IsOpened = true;
            return IsOpened;
        }
        public virtual void Close()
        { IsOpened = false; }

        public StatusCode ParseAndExecuteOneLine(string line, List<string> outmsg)
        {
            var tokens = line.Split(separators, StringSplitOptions.RemoveEmptyEntries).ToList<string>();
            if (tokens.Count<1)
                return StatusCode.Success;
            if (!Handlers.ContainsKey(tokens[0])) {
                return StatusCode.EUnknownCommand;
            }
            Log("Execute " + tokens[0]);
            var stt = Handlers[tokens[0]](tokens, outmsg);
            return stt;
        }
        public virtual bool ProcessOneMessage()
        {
            return true;
        }
        public virtual bool SendOutput(string text)
        {
            return true;
        }
    }
}
