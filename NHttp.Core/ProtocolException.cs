using System;
using System.Runtime.Serialization;

namespace NHttp
{
    [Serializable]
    internal class ProtocolException : Exception
    {
        public ProtocolException()
        {
        }

        public ProtocolException(string message)
            : base(message)
        {
        }

        public ProtocolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected ProtocolException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}