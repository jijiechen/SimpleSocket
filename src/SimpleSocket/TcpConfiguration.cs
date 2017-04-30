namespace SimpleSocket
{
    public static class TcpConfiguration
    {
        public static int SocketCloseTimeoutMs = 500;

        public static int AcceptBacklogCount = 1000;
        public static int ConcurrentAccepts = 1;
        public static int AcceptPoolSize = ConcurrentAccepts * 2;

        public static int ConnectPoolSize = 32;
        public static int SendReceivePoolSize = 512;

        public static int BufferChunksCount = 512;
        public static int SocketBufferSize = 8 * 1024;
    }
}
