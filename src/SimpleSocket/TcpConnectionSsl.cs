using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using SimpleSocket.Logging;
using System.Collections.Concurrent;

namespace SimpleSocket
{
    class TcpConnectionSsl : TcpConnectionBase, ITcpConnection
    {
        private static readonly ILogger Log = LogManager.GetLoggerFor<TcpConnectionSsl>();

        public static ITcpConnection CreateConnectingConnection(Guid connectionId, 
                                                                IPEndPoint remoteEndPoint, 
                                                                string targetHost,
                                                                bool validateServer,
                                                                TcpClientConnector connector, 
                                                                TimeSpan connectionTimeout,
                                                                Action<ITcpConnection> onConnectionEstablished, 
                                                                Action<ITcpConnection, SocketError> onConnectionFailed,
                                                                bool verbose)
        {
            var connection = new TcpConnectionSsl(connectionId, remoteEndPoint, verbose);
            // ReSharper disable ImplicitlyCapturedClosure
            connector.InitConnect(remoteEndPoint,
                                  (_, socket) =>
                                  {
                                      connection.InitClientSocket(socket, targetHost, validateServer, verbose);
                                      if (onConnectionEstablished != null)
                                          onConnectionEstablished(connection);
                                  },
                                  (_, socketError) =>
                                  {
                                      if (onConnectionFailed != null)
                                          onConnectionFailed(connection, socketError);
                                  }, connection, connectionTimeout);
            // ReSharper restore ImplicitlyCapturedClosure
            return connection;
        }

        public static ITcpConnection CreateClientFromSocket(Guid connectionId, 
                                                            IPEndPoint remoteEndPoint, 
                                                            Socket socket, 
                                                            string targetHost,
                                                            bool validateServer,
                                                            bool verbose)
        {
            var connection = new TcpConnectionSsl(connectionId, remoteEndPoint, verbose);
            connection.InitClientSocket(socket, targetHost, validateServer, verbose);
            return connection;
        }

        public static ITcpConnection CreateServerFromSocket(Guid connectionId,
                                                            IPEndPoint remoteEndPoint,
                                                            Socket socket,
                                                            X509Certificate certificate,
                                                            bool verbose)
        {
            Ensure.NotNull(certificate, "certificate");
            var connection = new TcpConnectionSsl(connectionId, remoteEndPoint, verbose);
            connection.InitServerSocket(socket, certificate, verbose);
            return connection;
        }

        public event Action<ITcpConnection, SocketError> ConnectionClosed;
        public Guid ConnectionId { get { return _connectionId; } }
        public int SendQueueSize { get { return _sendQueue.Count; } }
        public string ClientConnectionName { get { return _clientConnectionName; } }

        private readonly Guid _connectionId;
        private readonly bool _verbose;
        public string _clientConnectionName;

        private readonly ConcurrentQueue<ArraySegment<byte>> _sendQueue = new ConcurrentQueue<ArraySegment<byte>>();
        private readonly ConcurrentQueue<ReceivedData> _receiveQueue = new ConcurrentQueue<ReceivedData>();
        private readonly MemoryStream _memoryStream = new MemoryStream();

        private readonly object _streamLock = new object();
        private bool _isSending;
        private int _receiveHandling;
        private int _isClosed;

        private Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> _receiveCallback;

        private SslStream _sslStream;
        private bool _isAuthenticated;
        private int _sendingBytes;
        private bool _validateServer;
        private readonly byte[] _receiveBuffer = new byte[TcpConnection.BufferManager.ChunkSize];

        private TcpConnectionSsl(Guid connectionId, IPEndPoint remoteEndPoint, bool verbose): base(remoteEndPoint)
        {
            Ensure.NotEmptyGuid(connectionId, "connectionId");

            _connectionId = connectionId;
            _verbose = verbose;
        }

        private void InitServerSocket(Socket socket, X509Certificate certificate, bool verbose)
        {
            Ensure.NotNull(certificate, "certificate");

            InitConnectionBase(socket);
            if (verbose) Console.WriteLine("TcpConnectionSsl::InitClientSocket({0}, L{1})", RemoteEndPoint, LocalEndPoint);

            lock (_streamLock)
            {
                try
                {
                    socket.NoDelay = true;
                }
                catch (ObjectDisposedException)
                {
                    CloseInternal(SocketError.Shutdown, "Socket is disposed.");
                    return;
                }

                _sslStream = new SslStream(new NetworkStream(socket, true), false);
                try
                {
                    var enabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11;
                    _sslStream.BeginAuthenticateAsServer(certificate, false, enabledSslProtocols, true, OnEndAuthenticateAsServer, _sslStream);
                }
                catch (AuthenticationException exc)
                {
                    Log.InfoException(exc, "[S{0}, L{1}]: Authentication exception on BeginAuthenticateAsServer.", RemoteEndPoint, LocalEndPoint);
                    CloseInternal(SocketError.SocketError, exc.Message);
                }
                catch (ObjectDisposedException)
                {
                    CloseInternal(SocketError.SocketError, "SslStream disposed.");
                }
                catch (Exception exc)
                {
                    Log.InfoException(exc, "[S{0}, L{1}]: Exception on BeginAuthenticateAsServer.", RemoteEndPoint, LocalEndPoint);
                    CloseInternal(SocketError.SocketError, exc.Message);
                }
            }
        }

        private void OnEndAuthenticateAsServer(IAsyncResult ar)
        {
            try
            {
                lock (_streamLock)
                {
                    var sslStream = (SslStream) ar.AsyncState;
                    sslStream.EndAuthenticateAsServer(ar);
                    if (_verbose)
                        DisplaySslStreamInfo(sslStream);
                    _isAuthenticated = true;
                }
                StartReceive();
                TrySend();
            }
            catch (AuthenticationException exc)
            {
                Log.InfoException(exc, "[S{0}, L{1}]: Authentication exception on EndAuthenticateAsServer.", RemoteEndPoint, LocalEndPoint);
                CloseInternal(SocketError.SocketError, exc.Message);
            }
            catch (ObjectDisposedException)
            {
                CloseInternal(SocketError.SocketError, "SslStream disposed.");
            }
            catch (Exception exc)
            {
                Log.InfoException(exc, "[S{0}, L{1}]: Exception on EndAuthenticateAsServer.", RemoteEndPoint, LocalEndPoint);
                CloseInternal(SocketError.SocketError, exc.Message);
            }
        }

        private void InitClientSocket(Socket socket, string targetHost, bool validateServer, bool verbose)
        {
            Ensure.NotNull(targetHost, "targetHost");

            InitConnectionBase(socket);
            if (verbose) Console.WriteLine("TcpConnectionSsl::InitClientSocket({0}, L{1})", RemoteEndPoint, LocalEndPoint);

            _validateServer = validateServer;

            lock (_streamLock)
            {
                try
                {
                    socket.NoDelay = true;
                }
                catch (ObjectDisposedException)
                {
                    CloseInternal(SocketError.Shutdown, "Socket is disposed.");
                    return;
                }

                _sslStream = new SslStream(new NetworkStream(socket, true), false, ValidateServerCertificate, null);
                try
                {
                    _sslStream.BeginAuthenticateAsClient(targetHost, OnEndAuthenticateAsClient, _sslStream);
                }
                catch (AuthenticationException exc)
                {
                    Log.InfoException(exc, "[S{0}, L{1}]: Authentication exception on BeginAuthenticateAsClient.", RemoteEndPoint, LocalEndPoint);
                    CloseInternal(SocketError.SocketError, exc.Message);
                }
                catch (ObjectDisposedException)
                {
                    CloseInternal(SocketError.SocketError, "SslStream disposed.");
                }
                catch (Exception exc)
                {
                    Log.InfoException(exc, "[S{0}, {1}]: Exception on BeginAuthenticateAsClient.", RemoteEndPoint, LocalEndPoint);
                    CloseInternal(SocketError.SocketError, exc.Message);
                }
            }
        }

        private void OnEndAuthenticateAsClient(IAsyncResult ar)
        {
            try
            {
                lock (_streamLock)
                {
                    var sslStream = (SslStream) ar.AsyncState;
                    sslStream.EndAuthenticateAsClient(ar);
                    if (_verbose)
                        DisplaySslStreamInfo(sslStream);
                    _isAuthenticated = true;
                }
                StartReceive();
                TrySend();
            }
            catch (AuthenticationException exc)
            {
                Log.InfoException(exc, "[S{0}, L{1}]: Authentication exception on EndAuthenticateAsClient.", RemoteEndPoint, LocalEndPoint);
                CloseInternal(SocketError.SocketError, exc.Message);
            }
            catch (ObjectDisposedException)
            {
                CloseInternal(SocketError.SocketError, "SslStream disposed.");
            }
            catch (Exception exc)
            {
                Log.InfoException(exc, "[S{0}, L{1}]: Exception on EndAuthenticateAsClient.", RemoteEndPoint, LocalEndPoint);
                CloseInternal(SocketError.SocketError, exc.Message);
            }
        }

        // The following method is invoked by the RemoteCertificateValidationDelegate. 
        public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (!_validateServer)
                return true;

            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
            Log.Error("[S{0}, L{1}]: Certificate error: {2}", RemoteEndPoint, LocalEndPoint, sslPolicyErrors);
            // Do not allow this client to communicate with unauthenticated servers. 
            return false;
        }

        private void DisplaySslStreamInfo(SslStream stream)
        {
            Log.Info("[S{0}, L{1}]", RemoteEndPoint, LocalEndPoint);
            Log.Info("Cipher: {0} strength {1}", stream.CipherAlgorithm, stream.CipherStrength);
            Log.Info("Hash: {0} strength {1}", stream.HashAlgorithm, stream.HashStrength);
            Log.Info("Key exchange: {0} strength {1}", stream.KeyExchangeAlgorithm, stream.KeyExchangeStrength);
            Log.Info("Protocol: {0}", stream.SslProtocol);
            Log.Info("Is authenticated: {0} as server? {1}", stream.IsAuthenticated, stream.IsServer);
            Log.Info("IsSigned: {0}", stream.IsSigned);
            Log.Info("Is Encrypted: {0}", stream.IsEncrypted);
            Log.Info("Can read: {0}, write {1}", stream.CanRead, stream.CanWrite);
            Log.Info("Can timeout: {0}", stream.CanTimeout);
            Log.Info("Certificate revocation list checked: {0}", stream.CheckCertRevocationStatus);

            X509Certificate localCert = stream.LocalCertificate;
            if (localCert != null)
                Log.Info("Local certificate was issued to {0} and is valid from {1} until {2}.",
                                localCert.Subject, localCert.GetEffectiveDateString(), localCert.GetExpirationDateString());
            else
                Log.Info("Local certificate is null.");

            // Display the properties of the client's certificate.
            X509Certificate remoteCert = stream.RemoteCertificate;
            if (remoteCert != null)
                Log.Info("Remote certificate was issued to {0} and is valid from {1} until {2}.",
                                remoteCert.Subject, remoteCert.GetEffectiveDateString(), remoteCert.GetExpirationDateString());
            else
                Log.Info("Remote certificate is null.");
        }

        public void EnqueueSend(IEnumerable<ArraySegment<byte>> data)
        {
            lock (_streamLock)
            {
                int bytes = 0;
                foreach (var segment in data)
                {
                    _sendQueue.Enqueue(segment);
                    bytes += segment.Count;
                }
                NotifySendScheduled(bytes);
            }
            TrySend();
        }

        private void TrySend()
        {
            lock (_streamLock)
            {
                if (_isSending || _sendQueue.Count == 0 || _sslStream == null || !_isAuthenticated) return;
                if (TcpConnectionMonitor.Default.IsSendBlocked()) return;
                _isSending = true;
            }

            _memoryStream.SetLength(0);

            ArraySegment<byte> sendPiece;
            while (_sendQueue.TryDequeue(out sendPiece))
            {
                _memoryStream.Write(sendPiece.Array, sendPiece.Offset, sendPiece.Count);
                if (_memoryStream.Length >= TcpConnection.MaxSendPacketSize)
                    break;
            }
            _sendingBytes = (int) _memoryStream.Length;

            try
            {
                NotifySendStarting(_sendingBytes);
                _sslStream.BeginWrite(_memoryStream.GetBuffer(), 0, _sendingBytes, OnEndWrite, null);
            }
            catch (SocketException exc)
            {
                Log.DebugException(exc, "SocketException '{0}' during BeginWrite.", exc.SocketErrorCode);
                CloseInternal(exc.SocketErrorCode, "SocketException during BeginWrite.");
            }
            catch (ObjectDisposedException)
            {
                CloseInternal(SocketError.SocketError, "SslStream disposed.");
            }
            catch (Exception exc)
            {
                Log.DebugException(exc, "Exception during BeginWrite.");
                CloseInternal(SocketError.SocketError, "Exception during BeginWrite");
            }
        }

        private void OnEndWrite(IAsyncResult ar)
        {
            try
            {
                _sslStream.EndWrite(ar);
                NotifySendCompleted(_sendingBytes);

                lock (_streamLock)
                {
                    _isSending = false;
                }
                TrySend();
            }
            catch (SocketException exc)
            {
                Log.DebugException(exc, "SocketException '{0}' during EndWrite.", exc.SocketErrorCode);
                NotifySendCompleted(0);
                CloseInternal(exc.SocketErrorCode, "SocketException during EndWrite.");
            }
            catch (ObjectDisposedException)
            {
                NotifySendCompleted(0);
                CloseInternal(SocketError.SocketError, "SslStream disposed.");
            }
            catch (Exception exc)
            {
                Log.DebugException(exc, "Exception during EndWrite.");
                NotifySendCompleted(0);
                CloseInternal(SocketError.SocketError, "Exception during EndWrite.");
            }
        }

        public void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback)
        {
            Ensure.NotNull(callback, "callback");

            if (Interlocked.Exchange(ref _receiveCallback, callback) != null)
            {
                Log.Fatal("ReceiveAsync called again while previous call wasn't fulfilled"); 
                throw new InvalidOperationException("ReceiveAsync called again while previous call wasn't fulfilled");
            }
            TryDequeueReceivedData();
        }

        private void StartReceive()
        {
            try
            {
                NotifyReceiveStarting();
                _sslStream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnEndRead, null);
            }
            catch (SocketException exc)
            {
                Log.DebugException(exc, "SocketException '{0}' during BeginRead.", exc.SocketErrorCode);
                CloseInternal(exc.SocketErrorCode, "SocketException during BeginRead.");
            }
            catch (ObjectDisposedException)
            {
                CloseInternal(SocketError.SocketError, "SslStream disposed.");
            }
            catch (Exception exc)
            {
                Log.DebugException(exc, "Exception during BeginRead.");
                CloseInternal(SocketError.SocketError, "Exception during BeginRead.");
            }
        }

        private void OnEndRead(IAsyncResult ar)
        {
            int bytesRead;
            try
            {
                bytesRead = _sslStream.EndRead(ar);
            }
            catch (SocketException exc)
            {
                Log.DebugException(exc, "SocketException '{0}' during EndRead.", exc.SocketErrorCode);
                NotifyReceiveCompleted(0);
                CloseInternal(exc.SocketErrorCode, "SocketException during EndRead.");
                return;
            }
            catch (ObjectDisposedException)
            {
                NotifyReceiveCompleted(0);
                CloseInternal(SocketError.SocketError, "SslStream disposed.");
                return;
            }
            catch (Exception exc)
            {
                Log.DebugException(exc, "Exception during EndRead.");
                NotifyReceiveCompleted(0);
                CloseInternal(SocketError.SocketError, "Exception during EndRead.");
                return;
            }
            if (bytesRead <= 0) // socket closed normally
            {
                NotifyReceiveCompleted(0);
                CloseInternal(SocketError.Success, "Socket closed.");
                return;
            }

            NotifyReceiveCompleted(bytesRead);

            var buffer = TcpConnection.BufferManager.CheckOut();
            if (buffer.Array == null || buffer.Count == 0 || buffer.Array.Length < buffer.Offset + buffer.Count)
                throw new Exception("Invalid buffer allocated.");
            Buffer.BlockCopy(_receiveBuffer, 0, buffer.Array, buffer.Offset, bytesRead);
            var buf = new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count);
            _receiveQueue.Enqueue(new ReceivedData(buf, bytesRead));

            StartReceive();
            TryDequeueReceivedData();
        }

        private void TryDequeueReceivedData()
        {
            if (Interlocked.CompareExchange(ref _receiveHandling, 1, 0) != 0)
                return;
            do
            {
                if (_receiveQueue.Count > 0 && _receiveCallback != null)
                {
                    var callback = Interlocked.Exchange(ref _receiveCallback, null);
                    if (callback == null)
                    {
                        Log.Fatal("Some threading issue in TryDequeueReceivedData! Callback is null!");
                        throw new Exception("Some threading issue in TryDequeueReceivedData! Callback is null!");
                    }

                    var res = new List<ReceivedData>(_receiveQueue.Count);
                    ReceivedData piece;
                    while (_receiveQueue.TryDequeue(out piece))
                    {
                        res.Add(piece);
                    }

                    var data = new ArraySegment<byte>[res.Count];
                    int bytes = 0;
                    for (int i = 0; i < data.Length; ++i)
                    {
                        var d = res[i];
                        bytes += d.DataLen;
                        data[i] = new ArraySegment<byte>(d.Buf.Array, d.Buf.Offset, d.DataLen);
                    }
                    callback(this, data);

                    for (int i = 0, n = res.Count; i < n; ++i)
                    {
                        TcpConnection.BufferManager.CheckIn(res[i].Buf); // dispose buffers
                    }
                    NotifyReceiveDispatched(bytes);
                }
                Interlocked.Exchange(ref _receiveHandling, 0);
            } while (_receiveQueue.Count > 0
                     && _receiveCallback != null
                     && Interlocked.CompareExchange(ref _receiveHandling, 1, 0) == 0);
        }

        public void Close(string reason)
        {
            CloseInternal(SocketError.Success, reason ?? "Normal socket close."); // normal socket closing
        }

        private void CloseInternal(SocketError socketError, string reason)
        {
            if (Interlocked.CompareExchange(ref _isClosed, 1, 0) != 0)
                return;

            NotifyClosed();

            if (_verbose)
            {
                Log.Info("{0} closed [{1:HH:mm:ss.fff}: R{2}, L{3}, {4:d}]:Close reason: [{5}] {6} " +
                            "Received bytes: {7}, Sent bytes: {8}, Receive calls: {9}, Receive callbacks: {10}, Send calls: {11}, callbacks: {12}",
                            GetType().Name, DateTime.UtcNow, RemoteEndPoint, LocalEndPoint, _connectionId,
                            socketError, reason,
                            TotalBytesReceived, TotalBytesSent,
                            ReceiveCalls, ReceiveCallbacks,
                            SendCalls, SendCallbacks);
            }

            if (_sslStream != null)
                Helper.EatException(() => _sslStream.Close());

            var handler = ConnectionClosed;
            if (handler != null)
                handler(this, socketError);
        }

        public void SetClientConnectionName(string clientConnectionName)
        {
            _clientConnectionName = clientConnectionName;
        }

        public override string ToString()
        {
            return "S" + RemoteEndPoint;
        }

        private struct ReceivedData
        {
            public readonly ArraySegment<byte> Buf;
            public readonly int DataLen;

            public ReceivedData(ArraySegment<byte> buf, int dataLen)
            {
                Buf = buf;
                DataLen = dataLen;
            }
        }
    }
}