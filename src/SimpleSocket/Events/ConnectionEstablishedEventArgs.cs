
namespace SimpleSocket.Events
{
    public class ConnectionEstablishedEventArgs
    {
        public ConnectionEstablishedEventArgs(TcpConnectionManager connectionManager)
        {
            this.Connection = connectionManager;
        }

        public TcpConnectionManager Connection { get; private set; }
    }
}
