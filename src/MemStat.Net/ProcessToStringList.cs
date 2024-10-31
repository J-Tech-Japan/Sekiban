using ResultBoxes;
using System.Diagnostics;
namespace MemStat.Net;

public class ProcessToStringList
{
    public static ResultBox<List<string>> GetProcessOutput(string fileName, string arguments)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(info);
            using var reader = process?.StandardOutput;

            var lines = new List<string>();
            do
            {
                var output = reader?.ReadLine();
                if (output is not null)
                {
                    lines.Add(output);
                } else
                {
                    break;
                }

            } while (true);

            return lines;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
