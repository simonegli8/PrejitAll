namespace EstrellasDeEsperanza.PrejitAll;

public static class Crossgen2
{
    public static readonly string Crossgen2Exe = OperatingSystem.IsWindows() ? "crossgen2.exe" : "crossgen2";

    private static void PrintHelp()
    {
        Console.WriteLine("Run this tool to prejit all *.dll in the current directory");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine($"  prejitall [--crossgen path/to/{Crossgen2Exe}] <args for {Crossgen2Exe}>");
        Console.WriteLine();
        Console.WriteLine($"NOTE: if no args are passed it will try to locate system's {Crossgen2Exe} automatically or download it.");
        Console.WriteLine();
    }

    public static async Task Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-?")) {
            PrintHelp();
            return;
        }
        var crossgenPath = args.Take(2).FirstOrDefault(arg => arg.EndsWith(Crossgen2Exe, StringComparison.OrdinalIgnoreCase) && File.Exists(arg));
        if (crossgenPath != null) {
            Logger.LogMessage($"Using crossgen2 from {crossgenPath}");
            args = args.Where(arg => arg != crossgenPath).ToArray();
        }
        var path = args.FirstOrDefault();
        args = args.Skip(1).ToArray();

        await PrejitAll(path, crossgenPath, args);
    }
    public static async Task PrejitAll(string? path, string? crossgenpath = null, params string[] customArgs) 
    {
        string customArg = string.Join(" ", customArgs.Select(a => $"\"{a}\""));

        long totalAdditionalSize = 0;

        path ??= Directory.GetCurrentDirectory();
        TempCrossgen? tempCrossgen = null;

        if (crossgenpath == null)
        {
            var runtimes = await DotnetInfo.GetCommonRuntimeConfig(path);
            var maxversion = runtimes
                .OrderByDescending(r => r)
                .FirstOrDefault();
            if (maxversion == null)
            {
                Logger.LogError("No *.runtimeconfig.json files found in path");
                return;
            }
            crossgenpath = DotnetInfo.FindExistingCrossgen2(maxversion.VersionId);
            if (crossgenpath == null)
            {
                tempCrossgen = await DotnetInfo.DownloadCrossgen2(maxversion.VersionId);
                if (tempCrossgen != null) crossgenpath = tempCrossgen.Filename;
                else return;
            }
        }

        using var temp = tempCrossgen;

        string[] allFiles = Directory.GetFiles(path, "*.dll");

        // Don't waste time on these on Windows:
        HashSet<string> knownNativeLibs = new HashSet<string>()
        {
            "clretwrc.dll",
            "clrgc.dll",
            "clrjit.dll",
            "coreclr.dll",
            "hostfxr.dll",
            "hostpolicy.dll",
            "Microsoft.DiaSymReader.Native.amd64.dll",
            "mscordaccore.dll",
            "mscordbi.dll",
            "mscorrc.dll",
            "msquic.dll",
            "System.IO.Compression.Native.dll"
        };

        allFiles = allFiles.Where(f => !knownNativeLibs.Contains(Path.GetFileName(f))).ToArray();

        if (!allFiles.Any())
        {
            Logger.LogWarning("No *.dll files in the current directory");
            return;
        }

        for (var index = 0; index < allFiles.Length; index++)
        {
            string file = allFiles[index];
            string managedLibName = Path.GetFileName(file);

            Logger.LogDebug("==========================================================");
            Logger.LogDebug($"= [{index + 1}/{allFiles.Length}] Prejitting \"{managedLibName}\"");
            Logger.LogDebug("==========================================================");

            var frameworkReferences = await DotnetInfo.GetCommonRuntimeConfig(path);
            var referenceArguments = string.Join(' ', frameworkReferences
                .Select(r => $"-r \"{r.Path}{Path.DirectorySeparatorChar}{r.VersionId}{Path.DirectorySeparatorChar}*.dll\""));

            string tempR2Rname = managedLibName + ".ni";
            await ProcessUtils.RunProcessAsync(crossgenpath, $"{managedLibName} {referenceArguments} -r *.dll --resilient -O -o {tempR2Rname} {customArg}", null, path);
            if (File.Exists(tempR2Rname))
            {
                long beforeSize = new FileInfo(managedLibName).Length;
                long afterSize = new FileInfo(tempR2Rname).Length;

                Logger.LogSuccess($"Compiled {managedLibName}, size delta: {((afterSize - beforeSize) / 1024.0f):F0}Kb");

                totalAdditionalSize += afterSize - beforeSize;

                File.Delete(file);
                File.Move(tempR2Rname, file);
            }
        }

        Logger.LogSuccess(" ");
        Logger.LogSuccess("==========================================================");
        Logger.LogSuccess($"= Done! Total size added: {(totalAdditionalSize / 1024 / 1024):F2}Mb");
        Logger.LogSuccess("==========================================================");
    }
}
