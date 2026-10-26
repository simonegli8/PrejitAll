using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace EstrellasDeEsperanza.PrejitAll;

public class Runtime: IComparable<Runtime>
{
    public string Name = "";
    public string VersionId = "";
    public Version Version => new Version(Regex.Match(VersionId, "[0-9.]+").Value);
    public bool IsBeta => !Regex.Match(VersionId, "^[0-9.]+$", RegexOptions.Singleline).Success;
    public string Path = "";

    public int CompareTo(Runtime? other)
    {
        if (other == null) return 1;
        var version = Version.CompareTo(other.Version);
        if (version != 0) return version;
        if (IsBeta != other.IsBeta)
        {
            if (IsBeta) return -1;
            else return 1;
        }
        return string.CompareOrdinal(VersionId, other.VersionId);
    }
}

public class TempCrossgen: IDisposable
{
    public string Filename;

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(Filename);
        Directory.Delete(dir, true);
    }
}
public static class DotnetInfo
{
    public static async Task<List<Runtime>> GetInstalledRuntimes()
    {
        string listRuntimes = await ProcessUtils.RunProcessAsync("dotnet", "--list-runtimes", verbose: false);
        return Regex.Matches(listRuntimes, @"^\s*(?<runtime>.*?)\s+(?<version>[0-9.]+)(?<versionbeta>.*?)\s+\[(?<path>.*?)\]\s*$", RegexOptions.Multiline)
            .OfType<Match>()
            .Select(m => new Runtime {
                Name = m.Groups["runtime"].Value,
                VersionId = m.Groups["version"].Value + m.Groups["versionbeta"].Value,
                Path = m.Groups["path"].Value
            })
            .ToList();
    }

    public static List<Runtime> ParseRuntimeConfig(string filename)
    {
        var content = File.ReadAllText(filename);
        var document = JsonDocument.Parse(content);

        var runtimeOptions = document.RootElement.GetProperty("runtimeOptions");

        if (runtimeOptions.TryGetProperty("framework", out var framework))
        {
            var name = framework.GetProperty("name").GetString();
            var versionid = framework.GetProperty("version").GetString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(versionid))
            {
                Logger.LogError($"runtimeconfig.json {filename} framework has no name or no version.");
                return new List<Runtime>();
            } else return new List<Runtime> { new Runtime {
                Name = name,
                VersionId = versionid
            } };
        }

        if (runtimeOptions.TryGetProperty("frameworks", out var frameworks))
        {
            return frameworks.EnumerateArray()
                .Select(framework =>
                {
                    var name = framework.GetProperty("name").GetString();
                    var versionid = framework.GetProperty("version").GetString();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(versionid))
                    {
                        Logger.LogError($"runtimeconfig.json {filename} framework '{name ?? ""}' has no name or no version.");
                        return null;
                    }
                    else return new Runtime { Name = name, VersionId = versionid };
                })
                .Where(r => r != null)
                .OfType<Runtime>()
                .ToList();
        }

        Logger.LogError($"runtimeconfig.json {filename} has no framework or frameworks section.");
        return new List<Runtime>();
    }

    public static List<Runtime> GetCommonRuntimeConfig(string path)
    {
        return Directory.EnumerateFiles(path, "*.json")
            .Where(file => file.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => ParseRuntimeConfig(file))
            .GroupBy(r => r.Name)
            .Select(g => g.OrderByDescending(r => r).FirstOrDefault())
            .OfType<Runtime>()
            .ToList();
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
    [SupportedOSPlatformGuard("linux")]
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
    [SupportedOSPlatformGuard("macos")]
    public static bool IsMac => RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);

    public static bool IsLinuxMusl
    {
        get
        {
            if (!IsLinux) return false;

            var info = new ProcessStartInfo("ldd");
            info.Arguments = "/bin/ls";
            info.RedirectStandardOutput = true;
            info.UseShellExecute = false;
            var p = Process.Start(info);
            return p.StandardOutput.ReadToEnd().Contains("musl");
        }
    }

    public static (string RuntimeID, string OS, string Arch) RuntimeID
    {
        get
        {
            var os = IsWindows ? "win" : (IsMac ? "osx" : IsLinux ? "linux" : "");
            var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            var id = IsWindows ? "windows" : os;
            if (IsLinuxMusl) id += $"-musl";
            return (RuntimeID: $"{id}-{arch}", OS: os, Arch: arch);
        }
    }

    public static string? FindExistingCrossgen2(string versionid)
    {
        var rid = RuntimeID;

        string crossgenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", $"microsoft.netcore.app.crossgen2.{rid.RuntimeID}", versionid, "tools", Crossgen2.Crossgen2Exe);
        if (File.Exists(crossgenPath)) return crossgenPath;
        return null;
    }

    public static async Task<TempCrossgen?> DownloadCrossgen2(string versionid)
    {
        var rid = RuntimeID;
        var url = $"https://www.nuget.org/api/v2/package/Microsoft.NETCore.App.Crossgen2.{rid.RuntimeID}/{versionid}";
        string tempfile = null;
        var tempdir = Path.Combine(Path.GetTempPath(), "HostPanelPro.Crossgen2");
        try
        {
            Logger.LogMessage("Downloading crossgen2...");
            tempfile = await DownloadFileAsync(url, "zip");
            await UnzipZipFile(tempfile, tempdir, file => file.StartsWith("tools/"));
            return new TempCrossgen() { Filename = Path.Combine(tempdir, Crossgen2.Crossgen2Exe) };
        } catch (Exception ex)
        {
            Logger.LogError($"Error downloading crossget2: {ex.Message}");
        } finally
        {
            if (tempfile != null) File.Delete(tempfile);
        }
        return null;
    }
    private static async Task<string> DownloadFileAsync(string url, string extension = null)
    {
        var web = new HttpClient();
        var tmp = Path.GetTempFileName();
        tmp = Path.ChangeExtension(tmp, extension ?? Path.GetExtension(url));
        using (HttpResponseMessage response = await web.GetAsync(url))
        {
            if (response.StatusCode != System.Net.HttpStatusCode.OK) throw new Exception($"Could not download file {url}. Status code: {response.StatusCode}");

            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                await response.Content.CopyToAsync(file);
            }
        }
        var name = Regex.Replace(url, @"(.*?/)|(?:\?.*$)", "", RegexOptions.Singleline);
        return tmp;
    }
    private static async Task UnzipZipFile(string zipFile, string destFolder, Func<string, bool> filter = null, Func<long, long, Task> progress = null)
    {
        try
        {
            Directory.CreateDirectory(destFolder);

            if (filter == null) filter = name => true;

            using (var file = new FileStream(zipFile, System.IO.FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(file))
            {
                long zipSize = file.Length;
                long unzipped = 0;

                int files = 0;

                foreach (var entry in zip.Entries)
                {
                    if (filter(entry.FullName))
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(Path.Combine(destFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        }
                        else
                        {
                            var filename = Path.Combine(destFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            var dir = Path.GetDirectoryName(filename);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            entry.ExtractToFile(filename, true);
                            files++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.Name)) files++;

                    unzipped += entry.CompressedLength;

                    if (zipSize != 0) await (progress?.Invoke(unzipped, zipSize) ?? Task.CompletedTask);
                }

                await (progress?.Invoke(zipSize, zipSize) ?? Task.CompletedTask);
            }
        }
        catch (Exception ex)
        {
            if (ex is ThreadAbortException)
                return;

            throw;
        }
    }
}
