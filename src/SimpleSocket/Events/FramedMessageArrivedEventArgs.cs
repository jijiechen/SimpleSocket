
using System;

namespace SimpleSocket.Events
{
    public class FramedMessageArrivedEventArgs
    {
        public FramedMessageArrivedEventArgs(TcpConnectionManager connectionManager, ArraySegment<byte> data)
        {
            this.Connection = connectionManager;
            this.Data = data;
        }

        public TcpConnectionManager Connection { get; private set; }
        public ArraySegment<byte> Data { get; private set; }
    }
}
