using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace OpenProtocolControllerEmulator
{
    public class SocketState
    {
        public Socket SourceSocket { get; private set; }
        public byte[] Buffer { get; private set; }

        public SocketState(Socket source)
        {
            SourceSocket = source;
            Buffer = new byte[16384];
        }
    }
}