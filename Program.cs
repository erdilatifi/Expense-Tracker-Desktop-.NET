using System.Diagnostics;

var projectPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..",
    "..",
    "..",
    "FinanceTracker",
    "FinanceTracker.csproj"));

var projectDirectory = Path.GetDirectoryName(projectPath)
    ?? throw new InvalidOperationException("FinanceTracker project directory could not be resolved.");

var configuration = "Debug";
var targetFramework = "net8.0-windows";
var executablePath = Path.Combine(
    projectDirectory,
    "bin",
    configuration,
    targetFramework,
    "FinanceTracker.exe");

if (IsProcessRunning(executablePath))
{
    Console.WriteLine("FinanceTracker is already running.");
    return 0;
}

var buildExitCode = await RunProcessAsync(
    "dotnet",
    $"build \"{projectPath}\" -c {configuration} --nologo",
    workingDirectory: projectDirectory);

if (buildExitCode != 0)
{
    return buildExitCode;
}

if (!File.Exists(executablePath))
{
    Console.Error.WriteLine($"Built executable was not found: {executablePath}");
    return 1;
}

var appExitCode = await RunProcessAsync(
    executablePath,
    string.Empty,
    workingDirectory: Path.GetDirectoryName(executablePath));

return appExitCode;

static bool IsProcessRunning(string executablePath)
{
    foreach (var process in Process.GetProcessesByName("FinanceTracker"))
    {
        try
        {
            if (string.Equals(process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    return false;
}

static async Task<int> RunProcessAsync(string fileName, string arguments, string? workingDirectory = null)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
    };

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine($"Failed to start process: {fileName}");
        return 1;
    }

    await process.WaitForExitAsync();
    return process.ExitCode;
}
