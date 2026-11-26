using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Lavagon.Core
{
    public static class Net
    {
        public static bool IsListening(int port)
        {
            if (port <= 0) return false;
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(ep => ep.Port == port);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>PID of the process listening on a TCP port (0 if unknown).</summary>
        public static int PortOwner(int port)
        {
            try
            {
                var r = Proc.Run("netstat.exe", "-ano -p TCP", Environment.SystemDirectory, 10000);
                foreach (var line in r.Output.Split('\n'))
                {
                    var m = Regex.Match(line, @"^\s*TCP\s+\S+:(\d+)\s+\S+\s+LISTENING\s+(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success && int.Parse(m.Groups[1].Value) == port) return int.Parse(m.Groups[2].Value);
                }
            }
            catch
            {
                // ignored
            }
            return 0;
        }

        public static string LocalIPv4()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    foreach (var ua in props.UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            return ua.Address.ToString();
                }
            }
            catch
            {
                // ignored
            }
            return "127.0.0.1";
        }
    }
}
