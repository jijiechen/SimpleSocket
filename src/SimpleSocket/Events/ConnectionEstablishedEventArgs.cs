
namespace SimpleSocket.Events
{
    public class ConnectionEstablishedEventArgs
    {
        public ConnectionEstablishedEventArgs(TcpConnectionManager connectionManager)
        {
            this.ConnectionManager = connectionManager;
        }

        public TcpConnectionManager ConnectionManager { get; private set; }
    }
}
