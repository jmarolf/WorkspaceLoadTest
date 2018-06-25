using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace ConstructFromBinLog
{
    class Program
    {
        static void Main(string[] args)
        {
            WriteOutCommandlineArgs(args[0], "commands.txt");
        }
        
        private static void WriteOutCommandlineArgs(string binLogPath, string outFilePath)
        {
            string binLogFilePath = binLogPath;
            var binLogReader = new BinaryLogReplayEventSource();
            TaskCommandLineEventArgs[] taskCommandLineEventArgs = binLogReader.ReadRecords(binLogFilePath).AsParallel()
                .Select(x => x.Args).OfType<TaskCommandLineEventArgs>().ToArray();
            var commandline = taskCommandLineEventArgs
                .Where(x => x.CommandLine.StartsWith(@"C:\Users\jmarolf\.nuget\packages\microsoft.net.compilers\2.9.0-beta7-63018-03\tools\vbc.exe") ||
                            x.CommandLine.StartsWith(@"C:\Users\jmarolf\.nuget\packages\microsoft.net.compilers\2.9.0-beta7-63018-03\tools\csc.exe"))
                .Select(x => x.CommandLine.StartsWith(@"C:\Users\jmarolf\.nuget\packages\microsoft.net.compilers\2.9.0-beta7-63018-03\tools\vbc.exe")
                            ? x.CommandLine.Replace(@"C:\Users\jmarolf\.nuget\packages\microsoft.net.compilers\2.9.0-beta7-63018-03\tools\vbc.exe", "vbc.exe")
                            : x.CommandLine.Replace(@"C:\Users\jmarolf\.nuget\packages\microsoft.net.compilers\2.9.0-beta7-63018-03\tools\csc.exe", "csc.exe"))
                .ToArray();

            File.WriteAllLines(outFilePath, commandline);
        }
    }
}
