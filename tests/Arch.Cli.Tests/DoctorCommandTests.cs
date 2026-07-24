using Arch.Cli.Commands;
using Arch.Cli.Tests.Fixtures;

namespace Arch.Cli.Tests;

public sealed class DoctorCommandTests
{
    [Fact]
    public async Task RunAsync_FailsConfigCheck_WhenNoArchYmlFound()
    {
        var tempDir = Directory.CreateTempSubdirectory("archcli-doctor-");
        try
        {
            var output = new CapturingOutputWriter();

            var exitCode = await DoctorCommand.RunAsync(null, tempDir.FullName, output);

            Assert.Equal(ExitCodes.EnvironmentError, exitCode);
            Assert.Contains(output.Lines, l => l.StartsWith('✘') && l.Contains("arch.yml found and valid"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FailsSolutionCheck_WhenSolutionFileMissing()
    {
        var tempDir = Directory.CreateTempSubdirectory("archcli-doctor-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "arch.yml"), "solution: DoesNotExist.sln");
            var output = new CapturingOutputWriter();

            var exitCode = await DoctorCommand.RunAsync(null, tempDir.FullName, output);

            Assert.Equal(ExitCodes.EnvironmentError, exitCode);
            Assert.Contains(output.Lines, l => l.StartsWith('✘') && l.Contains("Solution file exists"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
