using System;

namespace Probe
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string command = args.Length == 1 ? args[0] : null;
            if (command != "host" && command != "dump")
            {
                Console.Error.WriteLine("usage: Probe.exe host|dump");
                return 2;
            }
            try
            {
                if (command == "host")
                {
                    Host.Run(Game.Load());
                }
                else
                {
                    Dump.Run(Game.Load());
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e is ProbeException ? e.Message : e.ToString());
                Console.WriteLine("probe: failed");
                return 1;
            }
            finally
            {
                Console.Out.Flush();
            }
        }
    }

    internal sealed class ProbeException : Exception
    {
        public ProbeException(string message) : base(message)
        {
        }
    }
}
