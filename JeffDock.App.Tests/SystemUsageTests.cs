using System.ComponentModel;
using JeffDock.Plugins.System;

namespace JeffDock.App.Tests;

public class SystemUsageTests
{
    [Fact]
    public void CpuSubtractsIdleFromKernelAndUserDeltas()
    {
        Assert.Equal(25.0, WindowsCpuUsageReader.Calculate(new(100, 150, 50), new(175, 225, 75)));
        Assert.Equal(0.0, WindowsCpuUsageReader.Calculate(new(0, 0, 0), new(100, 100, 0)));
        Assert.Equal(100.0, WindowsCpuUsageReader.Calculate(new(0, 0, 0), new(0, 50, 50)));
        Assert.Null(WindowsCpuUsageReader.Calculate(new(100, 150, 50), new(100, 150, 50)));
        Assert.Null(WindowsCpuUsageReader.Calculate(new(100, 150, 50), new(0, 0, 0)));
    }

    [Fact]
    public void RamMeasuresPhysicalMemoryUsed()
    {
        Assert.Equal(75.0, WindowsMemoryUsage.Calculate(16000, 4000));
        Assert.Equal(0.0, WindowsMemoryUsage.Calculate(16000, 16000));
        Assert.Equal(100.0, WindowsMemoryUsage.Calculate(16000, 0));
        Assert.Null(WindowsMemoryUsage.Calculate(0, 0));
        Assert.Null(WindowsMemoryUsage.Calculate(10, 20));
    }

    [Fact]
    public void UsageImagesChangeOnlyForNewPercentagesAndRecoverFromFailedReadings()
    {
        double? reading = 23.1;
        var fail = false;
        using var source = new UsageStateSource("cpu", "CPU", () =>
            fail ? throw new Win32Exception() : reading);
        var changes = 0;
        source.StateChanged += (_, _) => changes++;
        var waiting = source.CurrentImageBytes;
        source.Poll();
        Assert.Equal("ready", source.CurrentState);
        Assert.False(waiting.SequenceEqual(source.CurrentImageBytes));
        var first = source.CurrentImageBytes;
        reading = 23.2;
        source.Poll();
        Assert.Equal(1, changes);
        Assert.Equal(first, source.CurrentImageBytes);
        fail = true;
        source.Poll();
        Assert.Equal("unavailable", source.CurrentState);
        fail = false;
        reading = 75;
        source.Poll();
        Assert.Equal("ready", source.CurrentState);
        Assert.Equal(3, changes);
        source.Dispose();
        reading = 10;
        source.Poll();
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task WindowsReadersReturnPercentagesOnThisMachine()
    {
        var cpu = new WindowsCpuUsageReader();
        Assert.Null(cpu.Read());
        await Task.Delay(150);
        Assert.InRange(cpu.Read()!.Value, 0, 100);
        Assert.InRange(WindowsMemoryUsage.Read()!.Value, 0, 100);
    }
}
