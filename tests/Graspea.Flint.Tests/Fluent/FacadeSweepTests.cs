using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Fluent;

/// <summary>
/// The reflection sweep over every generated facade (design §8.2, §10.3 unit: fluent): every channel and property of every
/// template in the committed IR has exactly the predicted method(s), no other overload shape exists, and the entries and
/// enums are the predicted ones. Names follow design §7.6, re-derived here from the IR; nothing is read from the
/// generator's output.
/// </summary>
public sealed partial class FacadeSweepTests
{
    private static readonly string[] ReservedCoreNames =
        ["Encode", "Series", "Encoding", "Property", "Title", "Subtitle", "BaseSize", "CanvasSize", "Annotate", "DisplayName", "Theme", "Options", "Configure", "Build", "Validate", "ToJson", "ToTemplate"];

    private static readonly Assembly Library = typeof(ChartBuilder<,>).Assembly;

    /// <summary>One template of the committed IR with its union of channels and properties across backends.</summary>
    public sealed record Template(string Name, string[] Backends, string[] Channels, Property[] Properties)
    {
        public override string ToString() => Name;
    }

    public sealed record Property(string Key, string[] Kinds, Option[] DiscreteOptions);

    /// <summary>A distinct option value of a discrete property with the label of its first declaration.</summary>
    public sealed record Option(JsonNode Value, string Label);

    public static IEnumerable<Template> Templates()
    {
        var backends = CommittedIr.Strings(CommittedIr.Catalog["backends"]);
        foreach (var t in CommittedIr.Catalog["chartTypes"]!.AsArray())
        {
            var declared = t!["backends"]!.AsObject();
            var present = backends.Where(declared.ContainsKey).ToArray();
            var channels = present.SelectMany(b => CommittedIr.Strings(declared[b]!["channels"])).Distinct().ToArray();
            var properties = present
                .SelectMany(b => declared[b]!["properties"]!.AsArray().Select(p => p!))
                .GroupBy(p => p["key"]!.GetValue<string>())
                .Select(g => new Property(
                    g.Key,
                    g.Select(p => p["type"]!.GetValue<string>()).Distinct().ToArray(),
                    DistinctOptions(g.SelectMany(p => p["options"]?.AsArray() ?? []))))
                .ToArray();
            yield return new Template(t["name"]!.GetValue<string>(), present, channels, properties);
        }
    }

    /// <summary>The option values in backend and declaration order, without duplicates and without the omitted option.</summary>
    private static Option[] DistinctOptions(IEnumerable<JsonNode?> options)
    {
        var seen = new HashSet<string>();
        return [.. options
            .Where(o => o!["omitted"] is null && seen.Add(o["value"]!.ToJsonString()))
            .Select(o => new Option(o!["value"]!.DeepClone(), o["label"]!.GetValue<string>()))];
    }

    // source: design §4.2 (GenericChartBuilder adds nothing to the core; Chart.Of reaches every template through the core
    // methods only)
    [Test]
    public void GenericChartBuilder_DeclaresNoPublicMethods()
    {
        Assert.That(typeof(GenericChartBuilder<>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly), Is.Empty);
    }

    // source: ir/flint-ir.json (48 chart types), design §4.2 (Chart.Of is the hand-written escape hatch) and §13.3.2 (six
    // entries per template and six Chart.Of overloads, two of them deferred)
    [Test]
    public void Chart_HasExactlyTheEntriesAndOf()
    {
        var templates = Templates().ToArray();
        var methods = typeof(Chart).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.That(methods.Select(m => m.Name).Distinct(), Is.EquivalentTo(templates.Select(t => Entry(t.Name)).Append("Of")));
        Assert.That(methods, Has.Length.EqualTo(6 * templates.Length + 6));
    }

    // source: ir/flint-ir.json and design §4.2 (one sealed builder per template), §3 (one enum per distinct
    // template-and-key discrete pair; a key whose kind differs across backends is a raw JsonNode setter instead) and §7.6
    // rule 3 (enum names, with the overrides.json enum renames)
    [Test]
    public void Assembly_HasExactlyTheBuildersAndEnums()
    {
        var templates = Templates().ToArray();
        var builders = Library.GetExportedTypes().Where(t => t.IsClass && t.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(ChartBuilder<,>));
        Assert.That(builders.Select(t => t.Name), Is.EquivalentTo(templates.Select(t => Pascal(t.Name) + "Builder`1").Append("GenericChartBuilder`1")));

        var enums = Library.GetExportedTypes().Where(t => t.IsEnum && t.Namespace == "Graspea.Flint" && t != typeof(FlintBackend));
        var expected = templates.SelectMany(t => t.Properties.Where(p => p.Kinds is ["discrete"]).Select(p => EnumName(t, p))).ToArray();
        Assert.That(enums.Select(t => t.Name), Is.EquivalentTo(expected));
    }

    // source: design §4.2 entry point (rows + RowOptions with RequiresUnreferencedCode and RequiresDynamicCode; rows +
    // JsonTypeInfo; ChartData; [Obsolete(error: true)] string), §13.3.2 (the deferred RowOptions entry, annotated, and the
    // deferred JsonTypeInfo entry), §7.6 rule 2 (entry names)
    [TestCaseSource(nameof(Templates))]
    public void Entries_HaveThePredictedSixOverloads(Template template)
    {
        var builder = BuilderType(template);
        var entries = typeof(Chart).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == Entry(template.Name)).ToArray();
        Assert.That(entries.Select(Shape), Is.EquivalentTo(new[]
        {
            $"<TRow>(IEnumerable<TRow>,RowOptions=null)->{builder.Name}",
            $"<TRow>(IEnumerable<TRow>,JsonTypeInfo<TRow>)->{builder.Name}",
            $"<TRow>(RowOptions=null)->{builder.Name}",
            $"<TRow>(JsonTypeInfo<TRow>)->{builder.Name}",
            $"(ChartData)->{builder.Name}",
            $"(String)->{builder.Name}",
        }));

        foreach (var entry in entries)
        {
            var reflection = entry.GetParameters().Any(p => p.ParameterType == typeof(RowOptions));
            Assert.That(entry.IsDefined(typeof(RequiresUnreferencedCodeAttribute)), Is.EqualTo(reflection), () => entry.ToString()!);
            Assert.That(entry.IsDefined(typeof(RequiresDynamicCodeAttribute)), Is.EqualTo(reflection), () => entry.ToString()!);
            var obsolete = entry.GetCustomAttribute<ObsoleteAttribute>();
            Assert.That(obsolete?.IsError ?? false, Is.EqualTo(entry.GetParameters()[0].ParameterType == typeof(string)), () => entry.ToString()!);
        }

        Assert.That(entries.Single(e => !e.IsGenericMethodDefinition && e.GetParameters()[0].ParameterType == typeof(ChartData)).ReturnType, Is.EqualTo(builder.MakeGenericType(typeof(object))));
    }

    // source: design §4.2 generated facades (sealed partial, internal constructor, the CRTP base) and §8.2 ("the facade
    // emitter emits the fixed overload set and nothing else, and the reflection sweep over all facades fails on any
    // other shape"); names per design §7.6 rules 3 and 6, from the committed IR
    [TestCaseSource(nameof(Templates))]
    public void Builder_HasExactlyThePredictedMethods(Template template)
    {
        var builder = BuilderType(template);
        Assert.That(builder.IsSealed, Is.True);
        Assert.That(builder.GetConstructors(), Is.Empty);
        var row = builder.GetGenericArguments()[0];
        Assert.That(builder.BaseType, Is.EqualTo(typeof(ChartBuilder<,>).MakeGenericType(builder.MakeGenericType(row), row)));

        var (channels, properties) = MemberNames(template);
        var expected = new List<string>();
        foreach (var channel in template.Channels)
        {
            var name = channels[channel];
            expected.Add($"{name}(String,SemanticType?=null)");
            expected.Add($"{name}(Expression<Func<TRow,Object>>,SemanticType?=null)");
            expected.Add($"{name}(ChartEncoding)");
        }

        foreach (var property in template.Properties)
        {
            expected.Add($"{properties[property.Key]}({ValueType(template, property)})");
        }

        var methods = builder.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.That(methods.Select(m => $"{m.Name}{Parameters(m)}"), Is.EquivalentTo(expected));
        Assert.That(methods.Where(m => m.ReturnType != builder.MakeGenericType(row)), Is.Empty);
    }

    // source: design §4.2 (every facade method is a one-liner over a core method: a channel writes {"field":…} under its
    // wire name; binary -> bool, continuous -> double; the enum's members are the union of the option values across
    // backends with Default = omit the key; a kind mismatch takes raw JSON), §7.6 rule 4 (member names), §8.2 (stable member
    // numbers), ir/flint-ir.json (the wire names and values) and core/normalize-properties.ts (an array option is written
    // as its label)
    [TestCaseSource(nameof(Templates))]
    public void Builder_WritesTheCatalogKeys(Template template)
    {
        var (channels, properties) = MemberNames(template);
        var entry = typeof(Chart).GetMethod(Entry(template.Name), [typeof(ChartData)])!;
        object NewBuilder() => entry.Invoke(null, [ChartData.FromJson([])])!;

        foreach (var channel in template.Channels)
        {
            var builder = NewBuilder();
            var method = builder.GetType().GetMethod(channels[channel], [typeof(string), typeof(SemanticType?)])!;
            var input = Build(method.Invoke(builder, ["f", null])!);
            Assert.That(input.ChartSpec.Encodings[channel].Encodings[0].Field, Is.EqualTo("f"), $"{template.Name} {channel}");
        }

        foreach (var property in template.Properties)
        {
            var builder = NewBuilder();
            var method = builder.GetType().GetMethods().Single(m => m.Name == properties[property.Key]);
            var parameter = method.GetParameters()[0].ParameterType;
            if (parameter.IsEnum)
            {
                // Members are matched by name (design §7.6 rule 4), not by position: their numbers are stable across
                // upstream releases (design §8.2), so an inserted value is not numbered in option order.
                Assert.That(Convert.ToInt32(Enum.Parse(parameter, "Default")), Is.Zero, $"{template.Name} {property.Key}");
                Assert.That(Enum.GetNames(parameter), Is.EquivalentTo(property.DiscreteOptions.Select(o => MemberName(template, property, o)).Prepend("Default")), $"{template.Name} {property.Key}");
                Assert.That(Enum.GetValues(parameter).Cast<object>().Select(Convert.ToInt32), Is.Unique, $"{template.Name} {property.Key}");
                foreach (var option in property.DiscreteOptions)
                {
                    var member = Enum.Parse(parameter, MemberName(template, property, option));
                    var written = Build(method.Invoke(builder, [member])!).ChartSpec.ChartProperties![property.Key];
                    // An array value is written as its label: upstream matches arrays by reference only (normalize-properties.ts).
                    var wire = option.Value is JsonArray ? JsonValue.Create(option.Label) : option.Value;
                    Assert.That(JsonNode.DeepEquals(written, wire), Is.True, $"{template.Name} {property.Key} {member}: {written?.ToJsonString()}");
                }

                Assert.That(Build(method.Invoke(builder, [Enum.Parse(parameter, "Default")])!).ChartSpec.ChartProperties, Is.Null, $"{template.Name} {property.Key} Default");
                continue;
            }

            object sample = parameter == typeof(bool) ? true : parameter == typeof(double) ? 1.5 : JsonValue.Create(true);
            var value = Build(method.Invoke(builder, [sample])!).ChartSpec.ChartProperties![property.Key];
            Assert.That(value!.ToJsonString(), Is.EqualTo(parameter == typeof(double) ? "1.5" : "true"), $"{template.Name} {property.Key}");
        }
    }

    private static ChartAssemblyInput Build(object builder) => (ChartAssemblyInput)builder.GetType().GetMethod("Build")!.Invoke(builder, null)!;

    private static Type BuilderType(Template template) =>
        Library.GetType($"Graspea.Flint.{Pascal(template.Name)}Builder`1") ?? throw new AssertionException($"No builder for {template.Name}.");

    private static string ValueType(Template template, Property property) => property.Kinds switch
    {
        ["discrete"] => EnumName(template, property),
        ["binary"] => "Boolean",
        ["continuous"] => "Double",
        _ => "JsonNode",
    };

    /// <summary>Design §7.6 rules 3 and 6: channels keep Pascal(key) (Channel suffix on a reserved core name); a property
    /// sharing a channel's name or a reserved core name gets Value; overrides.json template renames win.</summary>
    private static (Dictionary<string, string> Channels, Dictionary<string, string> Properties) MemberNames(Template template)
    {
        var renames = Overrides()["renames"]!.AsObject();
        string Renamed(string kind, string key, string fallback) => renames[$"{template.Name}/{kind}/{key}"]?.GetValue<string>() ?? fallback;
        var channels = template.Channels.ToDictionary(c => c, c =>
        {
            var name = Renamed("channel", c, Pascal(c));
            return ReservedCoreNames.Contains(name) ? name + "Channel" : name;
        });
        var properties = template.Properties.ToDictionary(p => p.Key, p =>
        {
            var name = Renamed("property", p.Key, Pascal(p.Key));
            return channels.ContainsValue(name) || ReservedCoreNames.Contains(name) ? name + "Value" : name;
        });
        return (channels, properties);
    }

    /// <summary>Design §7.6 rule 3: <c>{Pascal(chartType)}{Pascal(key)}</c>, unless overrides.json renames
    /// <c>&lt;Template&gt;/enum/&lt;key&gt;</c>.</summary>
    private static string EnumName(Template template, Property property) =>
        Overrides()["renames"]![$"{template.Name}/enum/{property.Key}"]?.GetValue<string>() ?? Pascal(template.Name) + Pascal(property.Key);

    /// <summary>Design §7.6 rule 4: a string value names the member (the literal <c>default</c> is DefaultLiteral, because
    /// Default omits the key); another value takes its label without a trailing <c>[…]</c> and <c>(default)</c>; a
    /// non-negative integer without a usable label is <c>N&lt;value&gt;</c>. overrides.json
    /// <c>renames["&lt;Template&gt;/enum/&lt;key&gt;/&lt;value&gt;"]</c> wins (the raw string, or the JSON of another value).</summary>
    private static string MemberName(Template template, Property property, Option option)
    {
        var raw = option.Value.GetValueKind() == System.Text.Json.JsonValueKind.String ? option.Value.GetValue<string>() : option.Value.ToJsonString();
        if (Overrides()["renames"]![$"{template.Name}/enum/{property.Key}/{raw}"]?.GetValue<string>() is { } renamed)
        {
            return renamed;
        }

        string NotDefault(string name) => name == "Default" ? "DefaultLiteral" : name;
        if (option.Value.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            return NotDefault(Pascal(option.Value.GetValue<string>()));
        }

        var label = DefaultMarker().Replace(TrailingBrackets().Replace(option.Label, ""), "");
        if (label.Any(char.IsAsciiLetterOrDigit))
        {
            return NotDefault(Pascal(label));
        }

        return option.Value.GetValueKind() == System.Text.Json.JsonValueKind.Number && option.Value.GetValue<double>() is >= 0 and var n && n == Math.Floor(n)
            ? $"N{n}"
            : throw new AssertionException($"Rule 4 cannot name {option.Value.ToJsonString()} (\"{option.Label}\").");
    }

    private static JsonNode Overrides() => JsonNode.Parse(File.ReadAllText(Path.Combine(CommittedIr.RepositoryRoot(), "generator", "overrides.json")))!;

    /// <summary>Design §7.6 rule 1: split on non-alphanumeric runs and case changes; an all-upper token of two or more
    /// letters is capitalised; a leading digit gets an underscore.</summary>
    private static string Pascal(string s)
    {
        var tokens = NonAlphanumeric().Split(s).SelectMany(part => CaseChange().Split(part)).Where(t => t.Length > 0);
        var name = string.Concat(tokens.Select(t => t.Length >= 2 && t.All(char.IsAsciiLetterUpper) ? t[0] + t[1..].ToLowerInvariant() : char.ToUpperInvariant(t[0]) + t[1..]));
        return char.IsAsciiDigit(name[0]) ? "_" + name : name;
    }

    /// <summary>Design §7.6 rule 2: Pascal(name) without one trailing Chart or Plot.</summary>
    private static string Entry(string chartType)
    {
        var full = Pascal(chartType);
        var last = NonAlphanumeric().Split(chartType.Trim()).Last(t => t.Length > 0);
        return last is "Chart" or "Plot" && full.Length > last.Length ? full[..^last.Length] : full;
    }

    private static string Shape(MethodInfo method)
    {
        var generic = method.IsGenericMethodDefinition ? $"<{string.Join(",", method.GetGenericArguments().Select(a => a.Name))}>" : "";
        return $"{generic}{Parameters(method)}->{method.ReturnType.Name}";
    }

    private static string Parameters(MethodInfo method) =>
        "(" + string.Join(",", method.GetParameters().Select(p => Format(p.ParameterType) + (p.IsOptional ? $"={p.DefaultValue ?? "null"}" : ""))) + ")";

    private static string Format(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Format(underlying) + "?";
        }

        return type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(",", type.GetGenericArguments().Select(Format))}>" : type.Name;
    }

    [GeneratedRegex(@"\s*\[[^\]]*\]\s*$")]
    private static partial Regex TrailingBrackets();

    [GeneratedRegex(@"\s*\(default\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultMarker();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CaseChange();
}
