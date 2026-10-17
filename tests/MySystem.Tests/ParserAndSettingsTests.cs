using MySystem.Core;
using MySystem.Gaming;
using MySystem.Infrastructure;
using Xunit;

namespace MySystem.Tests;

public sealed class ParserAndSettingsTests
{
    [Fact]
    public void PresentMonRequiresSchemaAndParsesQuotedApplicationNames()
    {
        var parser = new PresentMonCsvParser();
        Assert.Null(parser.Parse("game.exe,42,0x123,16.5"));
        Assert.Null(parser.Parse("Application,ProcessID,SwapChainAddress,MsBetweenPresents"));
        var frame = parser.Parse("\"game, test.exe\",42,0x123,16.5");
        Assert.NotNull(frame);
        Assert.Equal(42, frame.ProcessId);
        Assert.Equal("0x123", frame.SwapChain);
        Assert.Equal(16.5, frame.FrameTimeMs);
    }

    [Theory]
    [InlineData("app,42,0x123,NaN")]
    [InlineData("app,42,0x123,Infinity")]
    [InlineData("app,42,0x123,0")]
    [InlineData("app,42,0x123,-10")]
    [InlineData("app,0,0x123,16")]
    [InlineData("app,42")]
    [InlineData("\"unclosed,42,0x123,16")]
    public void PresentMonRejectsInvalidOrPartialRows(string row)
    {
        var parser = new PresentMonCsvParser();
        parser.Parse("Application,ProcessID,SwapChainAddress,MsBetweenPresents");
        Assert.Null(parser.Parse(row));
    }

    [Fact]
    public void PresentMonAdaptsToReorderedSchema()
    {
        var parser = new PresentMonCsvParser();
        parser.Parse("MsBetweenPresents,SwapChainAddress,ProcessID");
        Assert.Equal(new PresentedFrame(123, "0xabc", 25), parser.Parse("25,0xabc,123"));
    }

    [Fact]
    public void SettingsValidationBoundsPollingAndRejectsInvalidColors()
    {
        var settings = new AppSettings { RefreshSeconds = double.NaN, RenderFps = 1000, Accent = "javascript:alert(1)", OverlayOpacity = -2 };
        settings.Validate();
        Assert.Equal(1, settings.RefreshSeconds);
        Assert.Equal(60, settings.RenderFps);
        Assert.Equal("#41dbc1", settings.Accent);
        Assert.Equal(.2, settings.OverlayOpacity);
    }

    [Fact]
    public void SettingsRoundTripAndMalformedFileRecoveryPreserveTheOriginal()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MySystem.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder);
            store.Save(new() { Appearance = "light", RefreshSeconds = 2.5, EnableSensors = true });
            Assert.Equal(2.5, store.Load().RefreshSeconds);
            Assert.Equal("light", store.Load().Appearance);
            Assert.True(store.Load().EnableSensors);
            File.WriteAllText(store.FilePath, "{ damaged");
            Assert.Equal(1, store.Load().RefreshSeconds);
            Assert.NotNull(store.LastLoadError);
            Assert.Equal("{ damaged", File.ReadAllText(store.FilePath));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ExportUsesInvariantNumbersAndDoesNotTurnMissingSensorsIntoZero()
    {
        var now = DateTimeOffset.UtcNow;
        string csv = DataExport.MetricsCsv([
            new("test", "=formula", 12.5, "%", now, "Fixture", MetricAvailability.Available),
            Metric.Missing("temperature", "Temperature", "°C", now, "Fixture", "No sensor")]);
        Assert.Contains(",12.5,", csv);
        Assert.Contains("\"'=formula\"", csv);
        Assert.Contains("\"Temperature\",,\"°C\"", csv);
    }
}
