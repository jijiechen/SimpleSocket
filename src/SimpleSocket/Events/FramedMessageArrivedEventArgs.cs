
using System;

namespace SimpleSocket.Events
{
    public class FramedMessageArrivedEventArgs
    {
        public FramedMessageArrivedEventArgs(TcpConnectionManager connectionManager, ArraySegment<byte> data)
        {
            this.ConnectionManager = connectionManager;
            this.Data = data;
        }

        public TcpConnectionManager ConnectionManager { get; private set; }
        public ArraySegment<byte> Data { get; private set; }
    }
}
