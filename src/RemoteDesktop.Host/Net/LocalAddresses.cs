using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteDesktop.Host.Net;

/// <summary>Finds this machine's own IPv4 addresses, to display so the operator knows what to type
/// into the viewer.</summary>
public static class LocalAddresses
{
    public static List<string> IPv4()
    {
        var result = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    result.Add(unicast.Address.ToString());
            }
        }
        if (result.Count == 0) result.Add("(no network address found)");
        return result;
    }
}
