using System.Text.Json;
using System.Text.Json.Serialization;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Theming;

namespace Graspea.Flint.Tests.Serialization;

[JsonSerializable(typeof(ChartAssemblyInput))]
internal sealed partial class ThemeHostContext : JsonSerializerContext;

public sealed class ChartThemeConverterTests
{
    private static string Write(ChartTheme theme) => JsonSerializer.Serialize(theme);

    private static ChartTheme Read(string json) => JsonSerializer.Deserialize<ChartTheme>(json)!;

    // source: d.ts T:2916-3027 (theme_spec: ThemeSpec | string, a preset id) and design §10.4 test 14
    [Test]
    public void Write_Preset_IsTheIdString()
    {
        Assert.That(Write(ThemePreset.Economist), Is.EqualTo("\"economist\""));
    }

    // source: d.ts ThemeSpec.ink (T:3013 theme tree) and design §10.4 test 14
    [Test]
    public void Write_Spec_IsAnObject()
    {
        Assert.That(Write(new ThemeSpec { Ink = new ThemeInk { Accent = "#c00" } }), Is.EqualTo("""{"ink":{"accent":"#c00"}}"""));
    }

    // source: design §5.1 table (ThemeSpec | string: both read)
    [Test]
    public void Read_String_IsPreset()
    {
        var theme = Read("\"nyt\"");
        Assert.That(theme.Preset, Is.EqualTo(ThemePreset.Nyt));
        Assert.That(theme.Spec, Is.Null);
    }

    // source: design §5.1 table (ThemeSpec | string: both read)
    [Test]
    public void Read_Object_IsSpec()
    {
        var theme = Read("""{"extends":"economist","ink":{"accent":"#c00"}}""");
        Assert.That(theme.Preset, Is.Null);
        Assert.That(theme.Spec!.Extends, Is.EqualTo("economist"));
        Assert.That(theme.Spec.Ink!.Accent, Is.EqualTo("#c00"));
    }

    // source: design §5.2 (an unknown preset id round-trips verbatim; the validator reports it)
    [Test]
    public void Read_UnknownPreset_RoundTripsVerbatim()
    {
        Assert.That(Write(Read("\"house-style\"")), Is.EqualTo("\"house-style\""));
    }

    // source: design §5.1 (malformed input and an empty string-backed value are JsonExceptions)
    [TestCase("42", TestName = "Read_Number_ThrowsJsonException")]
    [TestCase("\"\"", TestName = "Read_EmptyString_ThrowsJsonException")]
    [TestCase("[]", TestName = "Read_Array_ThrowsJsonException")]
    public void Read_Malformed_ThrowsJsonException(string json)
    {
        Assert.That((Action)(() => Read(json)), Throws.InstanceOf<JsonException>());
    }

    // source: design §4.1 (implicit operators from ThemePreset and ThemeSpec; a null spec is an argument error)
    [Test]
    public void ImplicitOperators_SetTheMatchingForm()
    {
        ChartTheme preset = ThemePreset.Economist;
        Assert.That((preset.Preset, preset.Spec), Is.EqualTo(((ThemePreset?)ThemePreset.Economist, (ThemeSpec?)null)));
        Assert.That((Action)(() => { ChartTheme theme = (ThemeSpec)null!; }), Throws.ArgumentNullException);
    }

    // source: design §5.1 (host-options guarantee: hostile reflection options and a host source-generated context)
    [Test]
    public void Write_InDocument_UnderHostileHostOptions_IsIdentical()
    {
        foreach (var theme in new ChartTheme[] { ThemePreset.PowerBiLight, new ThemeSpec { Ink = new() { Surface = PlotColor() } } })
        {
            var input = new ChartAssemblyInput { Data = ChartData.FromJson([]), ChartSpec = new() { ChartType = ChartType.BarChart }, ThemeSpec = theme };
            var canonical = FlintJson.Serialize(input);
            Assert.That(JsonSerializer.Serialize(input, HostileOptions.Create()), Is.EqualTo(canonical));
            Assert.That(JsonSerializer.Serialize(input, ThemeHostContext.Default.ChartAssemblyInput), Is.EqualTo(canonical));
        }
    }

    private static ThemeInkSurface PlotColor() => new() { Plot = "#fafafa" };
}
