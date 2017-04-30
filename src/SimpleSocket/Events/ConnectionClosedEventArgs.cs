
using System.Net.Sockets;

namespace SimpleSocket.Events
{
    public class ConnectionClosedEventArgs
    {
        public ConnectionClosedEventArgs(TcpConnectionManager connectionManager, SocketError socketError)
        {
            this.ConnectionManager = connectionManager;
            this.SocketError = socketError;
        }

        public TcpConnectionManager ConnectionManager { get; private set; }
        public SocketError SocketError { get; private set; }
    }
}
