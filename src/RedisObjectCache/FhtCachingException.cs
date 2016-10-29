using System;

namespace RedisObjectCache
{
    [Serializable]
    public class FhtCachingException : Exception
    {
        public FhtCachingException()
        {
        }

        public FhtCachingException(string message)
            : base(message)
        {
        }

        public FhtCachingException(string message, Exception inner)
            : base(message, inner)
        {
        }

        protected FhtCachingException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }
    }
}