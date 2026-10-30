using System.Diagnostics;
using System.Text;

namespace EstrellasDeEsperanza.PrejitAll;

public static class ProcessUtils
{

    public static IEnumerable<string> Paths
    {
        get
        {
            string? proc, machine = "", user = "";
            string?[] sources;
            proc = System.Environment.GetEnvironmentVariable("PATH");
            if (DotnetInfo.IsWindows)
            {
                machine = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
                user = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
                var process = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                sources = new string?[] {
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.SystemX86),
                    process, machine, user };
            }
            else sources = new string[] { proc };

            return sources
                .SelectMany(paths => paths.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                .Where(path => path != null)
                .Select(path => path.Trim())
                .Distinct();
        }
    }

    public static string? Find(string cmd)
    {
        string? file = null;
        cmd = cmd.Trim('"');
        if (cmd.IndexOf(Path.DirectorySeparatorChar) >= 0)
        {
            if (File.Exists(cmd)) file = cmd;
        }
        else
        {
            if (DotnetInfo.IsWindows)
            {
                file = Paths
                      .SelectMany(p =>
                      {
                          var p1 = Path.Combine(p, cmd);
                          return new string[] { p1, p1 + ".exe" };
                      })
                      .FirstOrDefault(p => File.Exists(p));
            }
            else
            {
                file = Paths
                    .Select(p => Path.Combine(p, cmd))
                    .FirstOrDefault(p => File.Exists(p));
            }
        }
        return file;
    }

    public static async Task<string> RunProcessAsync(
        string path,
        string args = "",
        Dictionary<string, string>? envVars = null,
        string? workingDir = null,
        bool verbose = true,
        TimeSpan? timeout = null)
    {
        var sb = new StringBuilder();
        if (verbose)
            Logger.LogDebug($"{path} {args}");

        Process? process = null;
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = Find(path),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                Arguments = args,
            };

            if (workingDir != null)
                processStartInfo.WorkingDirectory = workingDir;

            if (envVars != null)
            {
                foreach (var envVar in envVars)
                    processStartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
            }

            process = Process.Start(processStartInfo)!;
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                Task.Run(async () =>
                {
                    await Task.Delay(300);
                    process.StandardInput.WriteLine();
                    process.Kill();
                });
                if (verbose)
                    Logger.LogError(e.Data);
            };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;

                sb.AppendLine(e.Data);
                if (e.Data?.Contains("Emitting R2R") == true)
                {
                    if (verbose) Logger.LogDebug(e.Data);
                    Logger.LogSuccess("Successfully prejitted.");
                }
                else if (e.Data?.Contains("No input files are loadable") == true)
                {
                    if (verbose) Logger.LogDebug(e.Data);
                    Logger.LogWarning("Not a managed lib");
                }
                else if (verbose) Logger.LogDebug(e.Data);
            };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
#if NETCOREAPP
        if (timeout != null)
        {
            using var cts = new CancellationTokenSource((TimeSpan)timeout);
            await process.WaitForExitAsync(cts.Token);
        }
        else await process.WaitForExitAsync();
#else
            if (DotnetInfo.IsCore)
            {
                var type = typeof(Process);
                var waitForExitAsync = type.GetMethod("WaitForExitAsync");
                Task task;
                if (timeout != null)
                {
                    using var cts = new CancellationTokenSource((TimeSpan)timeout);
                    task = (Task)waitForExitAsync.Invoke(process, new object[] {cts });
                } else {
                    task = (Task)waitForExitAsync.Invoke(process, Array.Empty<object>());                
                }
                await task;
            } else  {
                 if (timeout != null)
                {
                    process.WaitForExit((int)((TimeSpan)timeout).TotalMilliseconds);
                } else {
                    process.WaitForExit();
                }
            }
#endif
        }
        catch (Exception e)
        {
            if (verbose)
                Logger.LogError(e.Message);
}
        finally
        {
            if (process is { HasExited: false })
                process.Kill();
        }
        return sb.ToString();
    }
}
