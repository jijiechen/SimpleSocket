namespace SimpleSocket.Events
{
    public class FramedMessageArrivedEventArgs
    {
        public FramedMessageArrivedEventArgs(TcpConnectionManager connectionManager, byte[] data)
        {
            this.Connection = connectionManager;
            this.Data = data;
        }

        public TcpConnectionManager Connection { get; private set; }
        public byte[] Data { get; private set; }
    }
}
