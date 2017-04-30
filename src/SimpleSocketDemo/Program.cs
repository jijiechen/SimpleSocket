using SimpleSocket;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace SimpleSocketDemo
{
    class Program
    {
        static readonly ManualResetEvent _quitEvent = new ManualResetEvent(false);
        static readonly UTF8Encoding UTF8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        static void Main(string[] args)
        {
            Console.Title = "SimpleSocket Demo";
            Console.CancelKeyPress += Console_CancelKeyPress;

            var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 3666);

            var tcpService = new TcpService<EchoFramer>(endpoint, null);
            tcpService.ConnectionEstablished += (sender, ev) =>
            {
                Console.WriteLine("New connection connected from {0}", ev.Connection.RemoteEndPoint);
            };
            tcpService.ConnectionClosed += (sender, ev) =>
            {
                Console.WriteLine("Connection from {0} dropped", ev.Connection.RemoteEndPoint);
            };
            tcpService.MessageArrived += (sender, ev) =>
            {
                var message = UTF8NoBom.GetString(ev.Data);
                Console.WriteLine("Thread {2} Message from {0}: {1}", ev.Connection.RemoteEndPoint, message, Thread.CurrentThread.ManagedThreadId);

                ev.Connection.Send(ev.Data);
            };



            tcpService.Start();

            var clientThread = new Thread(ConnectAsClient);
            clientThread.Start();

            Console.WriteLine("Press Ctrl+C to quit.");
            _quitEvent.WaitOne();
            tcpService.Stop();
            clientThread.Join();
        }

        static void ConnectAsClient()
        {
            int count = 0;

            var server = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 3666);
            var connection = new TcpConnectionManager("ClientConnection", Guid.NewGuid(),
                server,
                new TcpClientConnector(),
                false,
                null,
                false,
                new EchoFramer(),
                (c, d) =>
                {
                    var message = UTF8NoBom.GetString(d);
                    Console.WriteLine("Client: message from server: {0}", message);

                    var messageCount = Interlocked.Increment(ref count);
                    c.Send(UTF8NoBom.GetBytes(string.Format("client says: {0} message received.", messageCount)));

                    if (messageCount == 50)
                    {
                        c.Close();
                    }
                },
                (c) => {
                    Console.WriteLine("Client: connected.");
                    c.Send(UTF8NoBom.GetBytes("Hello server."));
                },
                (c, e) => {
                    Console.WriteLine("Client: connection lost.");
                });

            connection.StartReceiving();
        }


        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Exiting...");
            _quitEvent.Set();
        }
    }

    class EchoFramer : IMessageFramer
    {
        private Action<byte[]> _receivedHandler;
        
        public void Cleanup()
        {
            
        }

        public void UnFrameData(IEnumerable<ArraySegment<byte>> data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            foreach (ArraySegment<byte> buffer in data)
            {
                Parse(buffer);
            }
        }

        public IEnumerable<ArraySegment<byte>> FrameData(ArraySegment<byte> data)
        {
            yield return data;
        }

        public void RegisterMessageArrivedCallback(Action<byte[]> handler)
        {
            if (handler == null)
                throw new ArgumentNullException("handler");

            _receivedHandler = handler;
        }
        private void Parse(ArraySegment<byte> bytes)
        {
            byte[] data = bytes.Array;

            var length = bytes.Count;
            var buffer = new byte[length];
            var bufferedLength = 0;

            for (int i = bytes.Offset, n = bytes.Offset + bytes.Count; i < n; i++)
            {
                int copyCnt = Math.Min(bytes.Count + bytes.Offset - i, length - bufferedLength);
                Buffer.BlockCopy(bytes.Array, i, buffer, 0, copyCnt);

                bufferedLength += copyCnt;
                i += copyCnt - 1;
            }

            if(_receivedHandler != null)
            {
                _receivedHandler(buffer);
            }
        }

    }
}
