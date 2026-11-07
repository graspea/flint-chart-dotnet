using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graspea.Flint.CSharp;
using Graspea.Flint.Serialization;

namespace Graspea.Flint.CSharp.Tests.Spec;

/// <summary>
/// Every entry of the generated name tables exists in Graspea.Flint with the stated signature (design §12.7, §12.10), so a
/// table that disagrees with the compiled library fails the build of that release.
/// </summary>
public sealed class NameTablesCrossCheckTests
{
    private static readonly Assembly Library = typeof(Chart).Assembly;

    public static IEnumerable<TestCaseData> Templates() => NameTables.Templates.Select(t => new TestCaseData(t.Name).SetName($"Template_MatchesTheLibrary({t.Name})"));

    // source: the compiled Graspea.Flint (ChartType constants, Chart entries, builder channel and property methods, enum
    // members and the JSON they write)
    [TestCaseSource(nameof(Templates))]
    public void Template_MatchesTheLibrary(string name)
    {
        var template = NameTables.Template(name)!;
        Assert.That(Constant<ChartType>(template.Constant).Name, Is.EqualTo(name));

        var entry = typeof(Chart).GetMethod(template.Entry, [typeof(ChartData)]);
        Assert.That(entry, Is.Not.Null, $"Chart.{template.Entry}(ChartData)");
        var builder = entry!.ReturnType;
        foreach (var channel in template.Channels)
        {
            Assert.That(builder.GetMethod(channel.Member, [typeof(string), typeof(SemanticType?)]), Is.Not.Null, $"{builder.Name}.{channel.Member}(string, SemanticType?)");
            var built = Build(template.Entry, b => builder.GetMethod(channel.Member, [typeof(string), typeof(SemanticType?)])!.Invoke(b, ["f", null]));
            Assert.That(built.ChartSpec.Encodings.Keys, Is.EqualTo(new[] { channel.Wire }), $"{builder.Name}.{channel.Member} binds {channel.Wire}");
        }

        foreach (var property in template.Properties)
        {
            var parameter = property.Kind switch
            {
                PropertyKind.Bool => typeof(bool),
                PropertyKind.Double => typeof(double),
                PropertyKind.Json => typeof(JsonNode),
                _ => Library.GetType($"Graspea.Flint.{property.EnumType}", throwOnError: true)!,
            };
            var method = builder.GetMethod(property.Method, [parameter]);
            Assert.That(method, Is.Not.Null, $"{builder.Name}.{property.Method}({parameter.Name})");
            foreach (var member in property.Members)
            {
                var value = Enum.Parse(parameter, member.Member);
                var built = Build(template.Entry, b => method!.Invoke(b, [value]));
                var written = built.ChartSpec.ChartProperties![property.Key]!;
                Assert.That(FluentWriter.Writes(member, JsonReader.Parse(written.ToJsonString())), Is.True, $"{property.EnumType}.{member.Member} writes {written.ToJsonString()}");
            }
        }
    }

    // source: the compiled Graspea.Flint (the static readonly constants of the string-backed structs)
    [Test]
    public void Constants_MatchTheLibrary()
    {
        Assert.Multiple(() =>
        {
            foreach (var c in NameTables.Channels) Assert.That(Constant<Channel>(c.Member).Name, Is.EqualTo(c.Wire));
            foreach (var c in NameTables.SemanticTypes) Assert.That(Constant<SemanticType>(c.Member).Name, Is.EqualTo(c.Wire));
            foreach (var c in NameTables.ThemePresets) Assert.That(Constant<ThemePreset>(c.Member).Name, Is.EqualTo(c.Wire));
            foreach (var c in NameTables.EncodingTypes) Assert.That(Constant<EncodingType>(c.Member).Name, Is.EqualTo(c.Wire));
            foreach (var c in NameTables.Aggregates) Assert.That(Constant<Aggregate>(c.Member).Name, Is.EqualTo(c.Wire));
            foreach (var c in NameTables.SortOrders) Assert.That(Constant<SortOrder>(c.Member).Name, Is.EqualTo(c.Wire));
            foreach (var c in NameTables.SeriesCountAxes) Assert.That(Constant<SeriesCountAxis>(c.Member).Name, Is.EqualTo(c.Wire));
        });
    }

    public static IEnumerable<TestCaseData> Classes() => NameTables.Classes.Select(c => new TestCaseData(c.Name).SetName($"Class_MatchesTheLibrary({c.Name})"));

    // source: the compiled Graspea.Flint (generated options and theming classes: [JsonPropertyName] and the declared type)
    [TestCaseSource(nameof(Classes))]
    public void Class_MatchesTheLibrary(string name)
    {
        var type = Library.GetType(name, throwOnError: true)!;
        foreach (var member in NameTables.Class(name)!.Members)
        {
            var property = type.GetProperty(member.Name);
            Assert.That(property, Is.Not.Null, $"{name}.{member.Name}");
            Assert.That(property!.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name, Is.EqualTo(member.Wire));
            Assert.That(TypeText(property!.PropertyType), Is.EqualTo(member.Type.Replace("?", string.Empty)), $"{name}.{member.Name}");
        }
    }

    public static IEnumerable<TestCaseData> UnionObjectForms() =>
        NameTables.UnionObjectForms.Select(c => new TestCaseData(c.Name).SetName($"UnionObjectForm_MatchesTheLibrary({c.Name})"));

    // source: the compiled Graspea.Flint (the hand-written theme union and its converter): every member of the table,
    // written under its wire name, is read into the property of that name and type
    [TestCaseSource(nameof(UnionObjectForms))]
    public void UnionObjectForm_MatchesTheLibrary(string name)
    {
        var type = Library.GetType(name, throwOnError: true)!;
        var members = NameTables.Class(name)!.Members;
        var json = "{" + string.Join(",", members.Select(m => $"\"{m.Wire}\":{(m.Type switch { "double" => "1", "double[]" => "[1]", "string[]" => "[\"a\"]", _ => "\"a\"" })}")) + "}";
        var value = System.Text.Json.JsonSerializer.Deserialize(json, type)!;
        Assert.That(type.GetProperty("Extensions")!.GetValue(value), Is.Null, json);
        foreach (var member in members)
        {
            var property = type.GetProperty(member.Name);
            Assert.That(property, Is.Not.Null, $"{name}.{member.Name}");
            Assert.That(TypeText(property!.PropertyType), Is.EqualTo(member.Type), $"{name}.{member.Name}");
            Assert.That(property.GetValue(value), Is.Not.Null, $"{name}.{member.Name}");
        }
    }

    public static IEnumerable<TestCaseData> StringUnions() =>
        NameTables.StringUnions.Select(u => new TestCaseData(u.Name).SetName($"StringUnion_MatchesTheLibrary({u.Name})"));

    // source: the compiled Graspea.Flint (a generated open literal union struct: a static readonly field per literal whose value
    // is the literal, and a converter that writes it as the plain string)
    [TestCaseSource(nameof(StringUnions))]
    public void StringUnion_MatchesTheLibrary(string name)
    {
        var type = Library.GetType(name, throwOnError: true)!;
        foreach (var literal in NameTables.StringUnion(name)!.Literals)
        {
            var value = type.GetField(literal.Member, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            Assert.That(value?.ToString(), Is.EqualTo(literal.Wire), $"{name}.{literal.Member}");
            Assert.That(System.Text.Json.JsonSerializer.Serialize(value, type), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(literal.Wire)), $"{name}.{literal.Member}");
        }
    }

    private static T Constant<T>(string member) => (T)typeof(T).GetField(member, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static ChartAssemblyInput Build(string entry, Action<object> configure)
    {
        var builder = typeof(Chart).GetMethod(entry, [typeof(ChartData)])!.Invoke(null, [ChartData.FromJson([])])!;
        configure(builder);
        return (ChartAssemblyInput)builder.GetType().GetMethod("Build")!.Invoke(builder, null)!;
    }

    /// <summary>The type as the name tables write it, without nullability.</summary>
    private static string TypeText(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "string";
        if (type == typeof(double)) return "double";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(JsonNode)) return "JsonNode";
        if (type.IsArray) return TypeText(type.GetElementType()!) + "[]";
        if (type == typeof(int)) return "int";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return $"Dictionary<{TypeText(type.GetGenericArguments()[0])}, {TypeText(type.GetGenericArguments()[1])}>";
        // Hand-written types that overrides.json names are written by their full name; generated ones (classes and the
        // open literal union structs) by their simple name.
        return type == typeof(ChartSize) || type == typeof(ContinuousMarkCrossSection)
            || (type.GetCustomAttribute<JsonConverterAttribute>() is not null && NameTables.StringUnion(type.FullName!) is null)
            ? type.FullName!
            : type.Name;
    }
}
