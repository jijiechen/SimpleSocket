using System;
using System.Net;
using System.Net.Sockets;

namespace SimpleSocket
{
    public interface ITcpConnector
    {
        ITcpConnection ConnectTo(Guid connectionId, IPEndPoint remoteEndPoint, TimeSpan connectionTimeout, Action<ITcpConnection> onConnectionEstablished, Action<ITcpConnection, SocketError> onConnectionFailed, bool verbose);

        ITcpConnection ConnectSslTo(Guid connectionId, IPEndPoint remoteEndPoint, TimeSpan connectionTimeout, string targetHost, bool validateServer, Action<ITcpConnection> onConnectionEstablished, Action<ITcpConnection, SocketError> onConnectionFailed, bool verbose);
    }
}
