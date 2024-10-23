// See https://aka.ms/new-console-template for more information
using MemStat.Net;
using System.Diagnostics;
Console.WriteLine("Hello, World!");



var info = new ProcessStartInfo
{
    FileName = "sh",
    Arguments = "-c \"vm_stat\"",
    RedirectStandardOutput = true,
    UseShellExecute = false,
    CreateNoWindow = true
};

using (var process = Process.Start(info))
using (var reader = process.StandardOutput)
{

    var lines = new List<string>();
    do
    {
        var output = reader.ReadLine();
        if (output is not null)
        {
            lines.Add(output);
            Console.WriteLine(output);
        } else
        {
            break;
        }

    } while (true);

    var vmStat = MacVmStat.Parse(lines, DateTime.UtcNow);
    // Console.WriteLine(vmStat);
    Console.WriteLine($"Memory Used Mac :{MacVmStat.MemoryUsagePercentage(vmStat):P0}");
}

var linuxFree = new List<string>
{
    "total        used        free      shared  buff/cache   available",
    "Mem:         8117696     1410500     2842004      114524     3865192     6504320",
    "Swap:        8117244           0     8117244"
};
var vmStatLinux = LinuxMemoryInfo.Parse(linuxFree);
Console.WriteLine($"Memory Used Linux :{LinuxMemoryInfo.MemoryUsagePercentage(vmStatLinux):P0}");
