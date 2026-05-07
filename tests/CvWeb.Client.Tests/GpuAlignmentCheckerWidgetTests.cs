using System.Globalization;
using Bunit;
using CvWeb.Client.Components.Widgets;

namespace CvWeb.Client.Tests;

public sealed class GpuAlignmentCheckerWidgetTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUICulture = CultureInfo.CurrentUICulture;

    public GpuAlignmentCheckerWidgetTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUICulture;
    }

    [Fact]
    public void RendersInitializingStateByDefault()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = context.Render<GpuAlignmentChecker>();

        Assert.Contains("Preparing GPU pipeline...", component.Markup);
        Assert.Contains("Drift: 0.000%", component.Markup);
    }

    [Fact]
    public async Task UpdatesMetricsAfterInteropCallback()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = context.Render<GpuAlignmentChecker>();

        await component.Instance.UpdateGpuAlignmentResult(
            changedBytes: 165888,
            comparedBytes: 33177600,
            mismatchedPixels: 41522,
            elapsedMilliseconds: 9.42,
            backend: "webgl2",
            textureWidth: 3840,
            textureHeight: 2160);

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Textures: 3840x2160", component.Markup);
            Assert.Contains("Backend: webgl2", component.Markup);
            Assert.Contains("Changed Bytes: 165,888", component.Markup);
            Assert.Contains("Byte compare complete:", component.Markup);
        });
    }
}
