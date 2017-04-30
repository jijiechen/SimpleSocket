using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SimpleSocket
{
    /// <summary>
    /// Encodes outgoing messages in frames and decodes incoming frames. 
    /// For decoding it uses an internal state, raising a registered 
    /// callback, once full message arrives
    /// </summary>
    public interface IMessageFramer
    {
        void UnFrameData(IEnumerable<ArraySegment<byte>> data);
        IEnumerable<ArraySegment<byte>> FrameData(ArraySegment<byte> data);

        void RegisterMessageArrivedCallback(Action<byte[]> handler);
        void Cleanup();
    }


    public class PackageFramingException : Exception
    {
        public PackageFramingException()
        {
        }

        public PackageFramingException(string message) : base(message)
        {
        }

        public PackageFramingException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected PackageFramingException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}