using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine(
                "Please provide required data example: dotnet App.dll <dns-server> <url>"
            );
            return;
        }

        string dnsServer = args[0];
        string url = args[1];
        Console.WriteLine($"Requesting {url} with DNS {dnsServer}");

        // Make a network request with custom DNS settings
        using (new TemporaryDns(dnsServer))
        {
            using (HttpClient client = new HttpClient())
            {
                // Make a GET request
                HttpResponseMessage response = await client.GetAsync(url);

                // Check if the request was successful (status code 200-299)
                if (response.IsSuccessStatusCode)
                {
                    // Read and print the content of the response
                    string content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine(content);
                }
                else
                {
                    Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
        }

        // DNS settings automatically revert to their original values after exiting the using block
    }
}

class TemporaryDns : IDisposable
{
    private string[] originalDnsAddresses;

    public static NetworkInterface GetActiveEthernetOrWifiNetworkInterface()
    {
        var Nic = NetworkInterface
            .GetAllNetworkInterfaces()
            .FirstOrDefault(
                a =>
                    a.OperationalStatus == OperationalStatus.Up
                    && (
                        a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                        || a.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                    )
                    && a.GetIPProperties()
                        .GatewayAddresses
                        .Any(g => g.Address.AddressFamily.ToString() == "InterNetwork")
            );

        return Nic;
    }

    public TemporaryDns(params string[] dnsAddresses)
    {
        OperatingSystem os = Environment.OSVersion;

        if (os.Platform == PlatformID.Win32NT)
        {
            SetDNSWin(dnsAddresses[0]);
        }
        else if (os.Platform == PlatformID.Unix)
        {
            originalDnsAddresses = GetCurrentDnsAddresses();
            SetDNSUnix(dnsAddresses);
        }
        else
        {
            Console.WriteLine("Unknown operating system");
        }
    }

    public void Dispose()
    {
        OperatingSystem os = Environment.OSVersion;

        if (os.Platform == PlatformID.Win32NT)
        {
            UnsetDNSWin();
        }
        else if (os.Platform == PlatformID.Unix)
        {
            UnsetDNSUnix(originalDnsAddresses);
        }
        else
        {
            Console.WriteLine("Unknown operating system");
            return;
        }

        Console.WriteLine("DNS Settings Reverted");
    }

    public string[] GetCurrentDnsAddresses()
    {
        var networkInterface = GetActiveEthernetOrWifiNetworkInterface();
        if (networkInterface != null)
        {
            Console.WriteLine("Chosen network interface: ", networkInterface.Name);
            var properties = networkInterface.GetIPProperties();
            var dnsAddresses = properties.DnsAddresses.Select(dns => dns.ToString()).ToArray();
            return dnsAddresses;
        }
        return null;
    }

    public void SetDNSWin(string DnsString)
    {
        string[] Dns = { DnsString };
        var CurrentInterface = GetActiveEthernetOrWifiNetworkInterface();
        if (CurrentInterface == null)
            return;

        ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
        ManagementObjectCollection objMOC = objMC.GetInstances();
        foreach (ManagementObject objMO in objMOC)
        {
            if ((bool)objMO["IPEnabled"])
            {
                if (objMO["Description"].ToString().Equals(CurrentInterface.Description))
                {
                    ManagementBaseObject objdns = objMO.GetMethodParameters(
                        "SetDNSServerSearchOrder"
                    );
                    if (objdns != null)
                    {
                        objdns["DNSServerSearchOrder"] = Dns;
                        objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
                    }
                }
            }
        }
    }

    public static void UnsetDNSWin()
    {
        var CurrentInterface = GetActiveEthernetOrWifiNetworkInterface();
        if (CurrentInterface == null)
            return;

        ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
        ManagementObjectCollection objMOC = objMC.GetInstances();
        foreach (ManagementObject objMO in objMOC)
        {
            if ((bool)objMO["IPEnabled"])
            {
                if (objMO["Description"].ToString().Equals(CurrentInterface.Description))
                {
                    ManagementBaseObject objdns = objMO.GetMethodParameters(
                        "SetDNSServerSearchOrder"
                    );
                    if (objdns != null)
                    {
                        objdns["DNSServerSearchOrder"] = null;
                        objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
                    }
                }
            }
        }
    }

    public void SetDNSUnix(params string[] dnsAddresses)
    {
        RunCommand($"systemd-resolve --set-dns={string.Join(',', dnsAddresses)}");
    }

    public void UnsetDNSUnix(string[] dnsAddresses)
    {
        if (dnsAddresses != null)
        {
            RunCommand($"systemd-resolve --reset-dns={string.Join(',', dnsAddresses)}");
        }
    }

    public static void RunCommand(string command)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = new Process { StartInfo = processStartInfo })
        {
            process.Start();
            process.WaitForExit();
        }
    }

    static bool IsPhysicalEthernetInterface(NetworkInterface networkInterface)
    {
        // Check if the interface is Ethernet, is UP, and is not a virtual or loopback interface
        return networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet
            && networkInterface.OperationalStatus == OperationalStatus.Up
            && !networkInterface.Description.ToLowerInvariant().Contains("virtual")
            && !networkInterface.Description.ToLowerInvariant().Contains("loopback")
            && networkInterface
                .GetIPProperties()
                .UnicastAddresses
                .Any(addr => !IPAddress.IsLoopback(addr.Address));
    }
}
