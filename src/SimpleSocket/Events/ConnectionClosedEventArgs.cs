
using System.Net.Sockets;

namespace SimpleSocket.Events
{
    public class ConnectionClosedEventArgs
    {
        public ConnectionClosedEventArgs(TcpConnectionManager connectionManager, SocketError socketError)
        {
            this.Connection = connectionManager;
            this.SocketError = socketError;
        }

        public TcpConnectionManager Connection { get; private set; }
        public SocketError SocketError { get; private set; }
    }
}
