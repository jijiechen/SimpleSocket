using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SimpleSocket.Logging;

namespace SimpleSocket
{
    /// <summary>
    /// Manager for individual TCP connection. It handles connection lifecycle,
    /// heartbeats, message framing and dispatch to the memory bus.
    /// </summary>
    public class TcpConnectionManager
    {
        public const int ConnectionQueueSizeThreshold = 50000;
        public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMilliseconds(1000);

        private static readonly ILogger Log = LogManager.GetLoggerFor<TcpConnectionManager>();

        public readonly Guid ConnectionId;
        public readonly string ConnectionName;
        public readonly IPEndPoint RemoteEndPoint;
        public IPEndPoint LocalEndPoint { get { return _tcpConnection.LocalEndPoint; } }
        public bool IsClosed { get { return _isClosed != 0; } }
        public int SendQueueSize { get { return _tcpConnection.SendQueueSize; } }

        private readonly ITcpConnection _tcpConnection;
        private readonly IMessageFramer _framer;
        private int _isClosed;
        
        private readonly Action<TcpConnectionManager, SocketError> _connectionClosed;
        private readonly Action<TcpConnectionManager> _connectionEstablished;
        private readonly Action<TcpConnectionManager, byte[]> _messageReceived;

        public TcpConnectionManager(string connectionName,
                                    ITcpConnection openedConnection,
                                    IMessageFramer framer,
                                    Action<TcpConnectionManager, byte[]> messageReceived,
                                    Action<TcpConnectionManager, SocketError> onConnectionClosed)
        {
            Ensure.NotNull(openedConnection, nameof(openedConnection));
            Ensure.NotNull(framer, nameof(framer));

            ConnectionName = connectionName;
            ConnectionId = openedConnection.ConnectionId;

            _framer = framer;
            _framer.RegisterMessageArrivedCallback(OnMessageArrived);

            _messageReceived = messageReceived;
            _connectionClosed = onConnectionClosed;

            RemoteEndPoint = openedConnection.RemoteEndPoint;
            _tcpConnection = openedConnection;
            _tcpConnection.ConnectionClosed += OnConnectionClosed;
            if (_tcpConnection.IsClosed)
            {
                OnConnectionClosed(_tcpConnection, SocketError.Success);
                return;
            }
        }

        public TcpConnectionManager(string connectionName, 
                                    Guid connectionId,
                                    IPEndPoint remoteEndPoint,
                                    ITcpConnector connector,
                                    bool useSsl,
                                    string sslTargetHost,
                                    bool sslValidateServer,
                                    IMessageFramer framer,
                                    Action<TcpConnectionManager, byte[]> messageReceived,
                                    Action<TcpConnectionManager> onConnectionEstablished,
                                    Action<TcpConnectionManager, SocketError> onConnectionClosed)
        {
            Ensure.NotEmptyGuid(connectionId, "connectionId");
            Ensure.NotNull(remoteEndPoint, "remoteEndPoint");
            Ensure.NotNull(connector, "connector");
            Ensure.NotNull(framer, nameof(framer));
            if (useSsl) Ensure.NotNull(sslTargetHost, "sslTargetHost");

            ConnectionName = connectionName;
            ConnectionId = connectionId;

            _framer = framer;
            _framer.RegisterMessageArrivedCallback(OnMessageArrived);

            _messageReceived = messageReceived;
            _connectionEstablished = onConnectionEstablished;
            _connectionClosed = onConnectionClosed;

            RemoteEndPoint = remoteEndPoint;

            _tcpConnection = useSsl 
                ? connector.ConnectSslTo(ConnectionId, remoteEndPoint, ConnectionTimeout, sslTargetHost, sslValidateServer, OnConnectionEstablished, OnConnectionFailed, true)
                : connector.ConnectTo(ConnectionId, remoteEndPoint, ConnectionTimeout, OnConnectionEstablished, OnConnectionFailed, true);

            _tcpConnection.ConnectionClosed += OnConnectionClosed;
            if (_tcpConnection.IsClosed)
            {
                OnConnectionClosed(_tcpConnection, SocketError.Success);
                return;
            }
        }

        private void OnConnectionEstablished(ITcpConnection connection)
        {
            Log.Info("Connection '{0}#{1:d}' to [{2}] established.", ConnectionName, ConnectionId, connection.RemoteEndPoint);
            
            var handler = _connectionEstablished;
            if (handler != null)
                handler(this);
        }

        private void OnConnectionFailed(ITcpConnection connection, SocketError socketError)
        {
            if (Interlocked.CompareExchange(ref _isClosed, 1, 0) != 0) return;

            Log.Info("Connection '{0}#{1:d}' to [{2}] failed: {3}.", ConnectionName, ConnectionId, connection.RemoteEndPoint, socketError);

            if (_connectionClosed != null)
                _connectionClosed(this, socketError);

            _framer.Cleanup();
        }

        private void OnConnectionClosed(ITcpConnection connection, SocketError socketError)
        {
            if (Interlocked.CompareExchange(ref _isClosed, 1, 0) != 0) return;
            Log.Info("Connection '{0}#{1:d}' [{2}] closed: {3}.", ConnectionName, ConnectionId, connection.RemoteEndPoint, socketError);

            if (_connectionClosed != null)
                _connectionClosed(this, socketError);

            _framer.Cleanup();
        }

        private void OnMessageArrived(byte[] data)
        {
            Log.Trace("Message arrived from connection '{0}#{1:d}' [{2}] length: {3}.", 
                     ConnectionName, ConnectionId, _tcpConnection.RemoteEndPoint, data.Length);

            try
            {
                if (_messageReceived != null)
                    _messageReceived(this, data);
            }
            catch (Exception ex)
            {
                Log.ErrorException(ex, "Error while handling message on connection '{0}#{1:d}' [{2}] error: {4}.",
                    ConnectionName, ConnectionId, _tcpConnection.RemoteEndPoint,  ex.Message);
                CloseConnectionOnError("Error while handling message");
            }
        }

        public void StartReceiving()
        {
            _tcpConnection.ReceiveAsync(OnRawDataReceived);
        }

        private void OnRawDataReceived(ITcpConnection connection, IEnumerable<ArraySegment<byte>> data)
        {
            try
            {
                _framer.UnFrameData(data);
            }
            catch (PackageFramingException exc)
            {
                CloseConnectionOnError(string.Format("Invalid TCP frame received. Error: {0}.", exc.Message));
                return;
            }
            _tcpConnection.ReceiveAsync(OnRawDataReceived);
        }

        void CloseConnectionOnError(string message)
        {
            Ensure.NotNull(message, "message");
            Log.Error("Closing connection '{0}#{1:d}' [R{2}, L{3}] due to error. Reason: {4}", ConnectionName, ConnectionId, RemoteEndPoint, LocalEndPoint,  message);
            _tcpConnection.Close(message);
        }

        public void Close(string reason = null)
        {
            Log.Trace("Closing connection '{0}#{1:d}' [R{2}, L{3}] cleanly.{4}", ConnectionName, ConnectionId, RemoteEndPoint, LocalEndPoint,  reason.IsEmptyString() ? string.Empty : " Reason: " + reason);
            _tcpConnection.Close(reason);
        }

        public void Send(byte[] bytes, bool checkQueueSize = true)
        {
            if (IsClosed)
                return;

            int queueSize;
            if (checkQueueSize && (queueSize = _tcpConnection.SendQueueSize) > ConnectionQueueSizeThreshold)
            {
                CloseConnectionOnError(string.Format("Connection queue size is too large: {0}.", queueSize));
                return;
            }

            var data = new ArraySegment<byte>(bytes);
            var framed = _framer.FrameData(data);
            _tcpConnection.EnqueueSend(framed);
        }
    }
}
