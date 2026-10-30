namespace EstrellasDeEsperanza.PrejitAll;

public static class Logger
{
    public static TextWriter Log = Console.Out;
    public static TextWriter Error = Console.Error;
    public static void LogDebug(string? msg)
        => LogMessage(msg);

    public static void LogSuccess(string? msg) => LogMessage(msg, ConsoleColor.Green);

    public static void LogWarning(string? msg) => LogMessage(msg, ConsoleColor.DarkYellow);

    public static void LogError(string? msg) => LogMessage(msg, ConsoleColor.Red, true);

    public static void LogMessage(string? msg) => Console.WriteLine(msg);

    private static readonly object SyncObj = new();

    public static void LogMessage(string? msg, ConsoleColor color, bool error = false)
    {
        lock (SyncObj)
        {
            if (!error && Log == Console.Out)
            {
                var currentColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(msg);
                Console.ForegroundColor = currentColor;
            }
            else if (error && Error == Console.Error)
            {
                var currentColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Error.WriteLine(msg);
                Console.ForegroundColor = currentColor;
            }
            else if (error) Error?.WriteLine(msg);
            else Log?.WriteLine(msg);
        }
    }
}
