using System.Net.Sockets;

namespace TomLonghurst.AsyncRedisClient;

internal class RedisSocket : Socket
{
    internal bool IsDisposed { get; private set; }
    public bool IsClosed { get; private set; }

    ~RedisSocket()
    {
        Close();
        Dispose();
    }
        
    internal RedisSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType) : base(addressFamily, socketType, protocolType)
    {
    }

    internal RedisSocket(SocketType socketType, ProtocolType protocolType) : base(socketType, protocolType)
    {
    }

    public new void Close()
    {
        IsClosed = true;
        base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            IsDisposed = true;
        }
            
        base.Dispose(disposing);
    }
}