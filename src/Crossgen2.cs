using System.Reflection;

namespace EstrellasDeEsperanza.PrejitAll;

public static class Crossgen2
{
    public static TimeSpan Timeout = TimeSpan.FromMinutes(5);
    public static readonly string Crossgen2Exe = DotnetInfo.IsWindows ? "crossgen2.exe" : "crossgen2";

#if Tool
    private static void PrintHelp()
    {
        Console.WriteLine("Run this tool to prejit all *.dll in the current directory");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine($"  prejitall [path] [path/to/{Crossgen2Exe}] [--nonative] [--verbose] <args for {Crossgen2Exe}>");
        Console.WriteLine();
        Console.WriteLine($"NOTE: if no args are passed it will try to locate system's {Crossgen2Exe} automatically or download it.");
        Console.WriteLine();
    }

    public static async Task Main(string[] args)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"prejitall {version!.ToString(3)}");

        if (args.Contains("--help") || args.Contains("-?")) {
            PrintHelp();
            return;
        }
        bool native = true, verbose = false;
        if (args.Contains("--nonative"))
        {
            native = false;
            args = args.Where(arg => arg != "--nonative").ToArray();
        }
        var crossgenPath = args.Take(2).FirstOrDefault(arg => arg.EndsWith(Crossgen2Exe, StringComparison.OrdinalIgnoreCase) && File.Exists(arg));
        if (crossgenPath != null) {
            Logger.LogMessage($"Using crossgen2 from {crossgenPath}");
            args = args.Where(arg => arg != crossgenPath).ToArray();
        }
        var path = args.FirstOrDefault();
        if (path != null && !path.StartsWith("--")) args = args.Skip(1).ToArray();
        else path = null;

        await PrejitAllAsync(path, crossgenPath, native, null, args);
    }
#endif

    public static async Task PrejitAllAsync(string? path, string? crossgenpath = null, bool native = true, CancellationToken? cancel = null, params string[] customArgs) 
    {
        bool verbose = false;
        if (customArgs.Contains("--verbose"))
        {
            verbose = true;
        }

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
            native &= maxversion.Version.Major >= 11; // only use --instruction-set native for crossgen2 v11 or above

            crossgenpath = DotnetInfo.FindExistingCrossgen2(maxversion.VersionId);
            if (crossgenpath == null)
            {
                tempCrossgen = await DotnetInfo.DownloadCrossgen2(maxversion.VersionId);
                if (tempCrossgen != null) crossgenpath = tempCrossgen.Filename;
                else return;
            }
        }

        //using var temp = tempCrossgen;
        
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

        var frameworkReferences = await DotnetInfo.GetCommonRuntimeConfig(path);
        var referenceArguments = string.Join(" ", frameworkReferences
            .Select(r => $"-r \"{r.Path}{Path.DirectorySeparatorChar}{r.VersionId}{Path.DirectorySeparatorChar}*.dll\""));

        var rid = DotnetInfo.RuntimeID;

        bool error = false;
        for (var index = 0; index < allFiles.Length; index++)
        {
            if (cancel?.IsCancellationRequested == true) break;

            string file = allFiles[index];
            string managedLibName = Path.GetFileName(file);

            if (verbose)
            {
                Logger.LogDebug("==========================================================");
                Logger.LogDebug($"= [{index + 1}/{allFiles.Length}] Prejitting \"{managedLibName}\"");
                Logger.LogDebug("==========================================================");
            }

            string tempR2Rname = managedLibName + ".ni";
            var tempR2Rfile = Path.Combine(path, tempR2Rname);
            var args = $"{managedLibName} {referenceArguments} -r *.dll --resilient -O -o {tempR2Rname} --targetos {rid.OS} --targetarch {rid.Arch}{(native ? " --instruction-set native" : "")} {customArg}";
            await ProcessUtils.RunProcessAsync(crossgenpath, args, null, path, verbose, Timeout);
            if (File.Exists(tempR2Rfile))
            {
                long beforeSize = new FileInfo(file).Length;
                long afterSize = new FileInfo(tempR2Rfile).Length;

                Logger.LogSuccess($"Compiled {managedLibName}, size delta: {((afterSize - beforeSize) / 1024.0f):F0}Kb, {((index+1)*100)/allFiles.Length}%");

                totalAdditionalSize += afterSize - beforeSize;

                File.Delete(file);
                File.Move(tempR2Rfile, file);
            }
            else
            {
                error = true;
            }
        }

        Logger.LogSuccess(" ");
        Logger.LogSuccess("==========================================================");
        Logger.LogSuccess($"= Done! Total size added: {(totalAdditionalSize / 1024 / 1024):F2}Mb");
        Logger.LogSuccess("==========================================================");
    }
}
