using System.Globalization;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class BaselineRollbackTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("sarif")]
    public void OutputOverflowRestoresPreviousBaseline(string format)
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");

        var output = new StringWriter(CultureInfo.InvariantCulture);
        var priorOutput = Console.Out;
        try
        {
            SetOutputSerializer((_, _) => new string('x', 16 * 1024 * 1024 + 1));
            Console.SetOut(output);
            var exitCode = CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--format", format, "--no-telemetry" });
            Assert.True(exitCode == 2, $"An actual-size {format} output overflow returned exit {exitCode}.");
        }
        finally
        {
            Console.SetOut(priorOutput);
            SetOutputSerializer(null);
        }

        Assert.True(approved.SequenceEqual(File.ReadAllBytes(baseline)), $"An actual-size {format} output overflow replaced the approved baseline.");
        Assert.True(!output.ToString().Contains("Entries:", StringComparison.Ordinal), $"An actual-size {format} output overflow emitted partial success output.");

    }

    [Fact]
    public void SerializationAndSinkFailuresRestorePreviousBaseline()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        try
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            try
            {
                SetOutputSerializer((_, _) => throw new InvalidOperationException("serializer failure"));
                Console.SetOut(output);
                Assert.True(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--format", "json", "--no-telemetry" }) == 2,
                    "A serializer failure did not return exit 2.");
            }
            finally
            {
                Console.SetOut(priorOutput);
                SetOutputSerializer(null);
            }

            Assert.True(approved.SequenceEqual(File.ReadAllBytes(baseline)) && !output.ToString().Contains("Entries:", StringComparison.Ordinal),
                "A serializer failure did not roll back the baseline transaction.");
        }
        finally
        {
            SetOutputSerializer(null);
        }

        try
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            try
            {
                SetOutputSink(_ => throw new InvalidOperationException("output failure"));
                Console.SetOut(output);
                Assert.True(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--format", "text", "--no-telemetry" }) == 2,
                    "An output sink failure did not return exit 2.");
            }
            finally
            {
                Console.SetOut(priorOutput);
                SetOutputSink(null);
            }

            Assert.True(approved.SequenceEqual(File.ReadAllBytes(baseline)) && !output.ToString().Contains("Entries:", StringComparison.Ordinal),
                "An output sink failure did not roll back the baseline transaction.");
        }
        finally
        {
            SetOutputSink(null);
        }


    }

}
