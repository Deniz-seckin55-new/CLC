using CLC;
using System.Net.Sockets;
public static class TcpClientServerCS
{
    public static Dictionary<string, Func<Scope, Variable[], Variable?>> Run(Scope scope)
    {
        Dictionary<string, Func<Scope, Variable[], Variable?>> extensionFunctions = new()
        {
            
        };

        CLCPublic.RegisterType(extensionFunctions, "tsc.tcpclient", typeof(TcpClient));
        CLCPublic.RegisterType(extensionFunctions, "tsc.tcplistener", typeof(TcpListener));

        return extensionFunctions;
    }

    public static Dictionary<string, Type> RunTypes()
    {
        Dictionary<string, Type> extensionTypes = new()
        {
            {"tsc.tcpclient", typeof(TcpClient)},
            {"tsc.tcplistener", typeof(TcpListener)},
        };
        return extensionTypes;
    }

    public static Dictionary<string, object> RunTypesDefault()
    {
        Dictionary<string, object> extensionTypeDefaults = new()
        {
            {"tsc.tcpclient", new TcpClient()},
            {"tsc.tcplistener", new TcpListener(System.Net.IPAddress.Any, 8080)},
        };
        return extensionTypeDefaults;
    }
}