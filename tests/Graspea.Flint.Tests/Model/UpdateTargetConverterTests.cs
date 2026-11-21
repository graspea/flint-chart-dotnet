using System.Text.Json;
using System.Text.Json.Nodes;
using Graspea.Flint.Tests.Support;

namespace Graspea.Flint.Tests.Model;

// D: = the flint-chart 1.1.1 d.ts, dist/types-DFtJpR3x.d.ts; S: = its src/core/target-selector.ts. The members that hold an
// UpdateTarget (the target of ChartUpdateOp annotate-text and set-annotation, the targets of set-style,
// EnclosureAnnotationSpec and FreeformCloneBody) are generated from flint-chart 1.1.1 on.
public sealed class UpdateTargetConverterTests
{
    private const string Resolved =
        """{"visual":{"kind":"mark","role":"bar"},"elements":[{"value":{"region":"Asia"},"records":[{"region":"Asia","sales":120}]}]}""";

    // source: D:43-50 and D:69-72 (SemanticTargetRef = {visual: {kind, role}, elements: SemanticElement[]}) and D:12-22
    // (SemanticElement = {value: Record<string, unknown>, records?: Record<string, unknown>[]})
    [Test]
    public void ResolvedTarget_RoundTrips()
    {
        var target = new UpdateTarget
        {
            Visual = new TargetVisual { Kind = "mark", Role = "bar" },
            Elements = [new SemanticElement { Value = new() { ["region"] = "Asia" }, Records = [new() { ["region"] = "Asia", ["sales"] = 120 }] }],
        };
        Assert.That(JsonSerializer.Serialize(target), Is.EqualTo(Resolved));

        var read = JsonSerializer.Deserialize<UpdateTarget>(Resolved)!;
        Assert.That(read.Select, Is.Null);
        Assert.That((read.Visual!.Kind, read.Visual.Role), Is.EqualTo(("mark", "bar")));
        Assert.That(read.Elements, Has.Length.EqualTo(1));
        Assert.That(read.Elements![0].Value!["region"]!.GetValue<string>(), Is.EqualTo("Asia"));
        Assert.That(read.Elements[0].Records![0]["sales"]!.GetValue<double>(), Is.EqualTo(120));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo(Resolved));
    }

    // source: D:73-88 (SemanticTargetSelector = {select: {key: Record<string, unknown>, visual?: Partial<visual>}} | {select:
    // {where, visual?}}); S:12-16 (a key's values read as one-value sets)
    [Test]
    public void KeySelector_RoundTrips()
    {
        const string json = """{"select":{"key":{"region":"Asia","year":2024}}}""";
        var target = UpdateTarget.Key(new() { ["region"] = "Asia", ["year"] = 2024 });
        Assert.That((target.Visual, target.Elements, target.Select!.Where, target.Select.Visual), Is.EqualTo(((TargetVisual?)null, (SemanticElement[]?)null, (Dictionary<string, FilterValue>?)null, (TargetVisual?)null)));
        Assert.That(JsonSerializer.Serialize(target), Is.EqualTo(json));

        var read = JsonSerializer.Deserialize<UpdateTarget>(json)!;
        Assert.That(read.Select!.Where, Is.Null);
        Assert.That(read.Select.Key!.Keys, Is.EqualTo(new[] { "region", "year" }));
        Assert.That(read.Select.Key["year"]!.GetValue<double>(), Is.EqualTo(2024));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo(json));

        var narrowed = UpdateTarget.Key(new() { ["region"] = "Asia" }, new TargetVisual { Kind = "path" });
        Assert.That(JsonSerializer.Serialize(narrowed), Is.EqualTo("""{"select":{"key":{"region":"Asia"},"visual":{"kind":"path"}}}"""));
    }

    // source: D:73-88 (where: Record<string, FilterValue>), D:487-495 (FilterValue = {in: unknown[]} | {range: [unknown,
    // unknown]}) and S:12-16 (a select with where describes a set)
    [Test]
    public void WhereSelector_RoundTrips()
    {
        const string json =
            """{"select":{"where":{"region":{"in":["Asia","Europe"]},"month":{"range":["2025-03","2025-06"]}},"visual":{"role":"bar"}}}""";
        var target = UpdateTarget.Where(
            new()
            {
                ["region"] = new FilterValue { In = ["Asia", "Europe"] },
                ["month"] = new FilterValue { Range = ["2025-03", "2025-06"] },
            },
            new TargetVisual { Role = "bar" });
        Assert.That(target.Select!.Key, Is.Null);
        Assert.That(JsonSerializer.Serialize(target), Is.EqualTo(json));

        var read = JsonSerializer.Deserialize<UpdateTarget>(json)!;
        Assert.That(read.Select!.Key, Is.Null);
        Assert.That(read.Select.Where!["region"].In!.Select(v => v!.GetValue<string>()), Is.EqualTo(new[] { "Asia", "Europe" }));
        Assert.That(read.Select.Where["region"].Range, Is.Null);
        Assert.That(read.Select.Where["month"].Range!.Select(v => v!.GetValue<string>()), Is.EqualTo(new[] { "2025-03", "2025-06" }));
        Assert.That(read.Select.Visual!.Role, Is.EqualTo("bar"));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo(json));
    }

    // source: D:12-22 and D:73-88 (field values, records and the in and range items are unknown: any JSON value, kept as
    // written) and design §5.1 (numbers strict: a number written as text stays text)
    [TestCase("""{"select":{"key":{"a":null,"b":[1,"x",null],"c":{"d":true},"e":1.50,"f":"5","g":false}}}""")]
    [TestCase("""{"select":{"where":{"a":{"in":[null,1e3,"2024-01-15",[2],{"k":1}]},"b":{"range":[null,"10"]},"c":{"range":[1,2,3]},"d":null}}}""")]
    [TestCase("""{"visual":{"kind":"mark","role":"point"},"elements":[{"value":{"x":0.10,"y":null},"records":[{"x":0.1},null]},null]}""")]
    public void AnyValues_RoundTripAsWritten(string json)
    {
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<UpdateTarget>(json)), Is.EqualTo(json));
    }

    // source: S:12-21 and src/validate/updates.ts:155 (a target with select is a selector, any other a resolved target; a
    // select with where is a set, any other a key): every shape a reader can tell apart is kept, including the ones upstream
    // reports as invalid_updates (src/validate/updates.ts:380-388)
    [TestCase("{}")]
    [TestCase("""{"select":{}}""")]
    [TestCase("""{"select":{"key":{"region":"Asia"},"where":{"region":{"in":["Asia"]}}}}""")]
    [TestCase("""{"select":{"key":{"region":"Asia"}},"visual":{"kind":"mark","role":"bar"},"elements":[]}""")]
    [TestCase("""{"select":{"where":{"region":{"in":["Asia"],"range":[1,2]}}}}""")]
    [TestCase("""{"select":{"where":{"region":{}}}}""")]
    [TestCase("""{"elements":[{}]}""")]
    public void MalformedForms_RoundTrip(string json)
    {
        Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<UpdateTarget>(json)), Is.EqualTo(json));
    }

    // source: design §5.2 (unknown keys round-trip through Extensions and are written last), at every object level
    [Test]
    public void UnknownMembers_RoundTripAtEveryLevel()
    {
        const string resolved = """{"visual":{"kind":"glyph","role":"r","layer":0},"elements":[{"value":{},"rank":2}],"note":"n"}""";
        var read = JsonSerializer.Deserialize<UpdateTarget>(resolved)!;
        Assert.That(read.Extensions!["note"].GetString(), Is.EqualTo("n"));
        Assert.That(read.Visual!.Kind, Is.EqualTo("glyph"), "any kind is kept");
        Assert.That(read.Visual.Extensions!["layer"].GetInt32(), Is.EqualTo(0));
        Assert.That(read.Elements![0].Extensions!["rank"].GetInt32(), Is.EqualTo(2));
        Assert.That(JsonSerializer.Serialize(read), Is.EqualTo(resolved));

        const string selector = """{"select":{"where":{"x":{"in":[1],"open":true}},"limit":3},"priority":1}""";
        var set = JsonSerializer.Deserialize<UpdateTarget>(selector)!;
        Assert.That(set.Extensions!["priority"].GetInt32(), Is.EqualTo(1));
        Assert.That(set.Select!.Extensions!["limit"].GetInt32(), Is.EqualTo(3));
        Assert.That(set.Select.Where!["x"].Extensions!["open"].GetBoolean(), Is.True);
        Assert.That(JsonSerializer.Serialize(set), Is.EqualTo(selector));

        var written = new UpdateTarget { Visual = new TargetVisual { Kind = "mark" }, Extensions = new() { ["future"] = JsonDocument.Parse("1").RootElement } };
        Assert.That(JsonSerializer.Serialize(written), Is.EqualTo("""{"visual":{"kind":"mark"},"future":1}"""));
    }

    // source: design §5.1 (host-options guarantee; converters ignore the caller's options: the dictionary keys, the
    // numbers in the field values and the absent members are written as without options)
    [Test]
    public void Serialize_UnderHostileOptions_WritesTheSameDocument()
    {
        object[] values =
        [
            JsonSerializer.Deserialize<UpdateTarget>(Resolved)!,
            new UpdateTarget
            {
                Visual = new TargetVisual { Kind = "mark", Role = "bar" },
                Elements = [new SemanticElement { Value = new() { ["Region"] = "Asia", ["Year"] = 2024 }, Records = [new() { ["SalesTotal"] = 1.5 }] }],
            },
            UpdateTarget.Key(new() { ["Region"] = "Asia", ["Year"] = 2024 }, new TargetVisual { Role = "bar" }),
            UpdateTarget.Where(new() { ["MonthIndex"] = new FilterValue { Range = [3, 6] }, ["Region"] = new FilterValue { In = ["Asia", 7] } }),
            new TargetVisual { Kind = "legend" },
            new TargetSelector { Key = new() { ["Year"] = 2024 } },
            new FilterValue { In = [1, null] },
            new SemanticElement { Value = new() { ["Year"] = 2024 } },
        ];
        foreach (var value in values)
        {
            Assert.That(JsonSerializer.Serialize(value, value.GetType(), HostileOptions.Create()), Is.EqualTo(JsonSerializer.Serialize(value, value.GetType())));
        }

        Assert.That(JsonSerializer.Serialize(values[3]), Is.EqualTo("""{"select":{"where":{"MonthIndex":{"range":[3,6]},"Region":{"in":["Asia",7]}}}}"""));
    }

    // source: design §5.1 (converters ignore the caller's options on read too: wire names are case-sensitive, so a member
    // spelt otherwise is an unknown member, and a number written as text stays text)
    [Test]
    public void Deserialize_UnderHostileOptions_ReadsTheSameDocument()
    {
        var options = HostileOptions.Create();
        options.PropertyNameCaseInsensitive = true;
        const string json = """{"Select":{"key":{"year":"2024"}},"select":{"key":{"year":"2024"}}}""";
        var read = JsonSerializer.Deserialize<UpdateTarget>(json, options)!;
        Assert.That(read.Extensions!.Keys, Is.EqualTo(new[] { "Select" }));
        Assert.That(read.Select!.Key!["year"]!.GetValueKind(), Is.EqualTo(JsonValueKind.String));
        Assert.That(JsonSerializer.Serialize(read, options), Is.EqualTo("""{"select":{"key":{"year":"2024"}},"Select":{"key":{"year":"2024"}}}"""));
    }

    // source: design §5.1 (a value of another shape is malformed input: JsonException); D:327-347 and D:201 (target and
    // targets[] hold the targets, select, visual and elements their members, where the FilterValues); the wording is the
    // converters' own, pinned here
    [TestCase(typeof(UpdateTarget), "\"Asia\"", "An update target (target, targets[i]) must be an object.")]
    [TestCase(typeof(UpdateTarget), "[]", "An update target (target, targets[i]) must be an object.")]
    [TestCase(typeof(UpdateTarget), "1", "An update target (target, targets[i]) must be an object.")]
    [TestCase(typeof(UpdateTarget), """{"select":"Asia"}""", "The select of an update target must be an object.")]
    [TestCase(typeof(UpdateTarget), """{"select":[]}""", "The select of an update target must be an object.")]
    [TestCase(typeof(UpdateTarget), """{"visual":"mark"}""", "The visual of an update target (visual, select.visual) must be an object.")]
    [TestCase(typeof(UpdateTarget), """{"select":{"key":{},"visual":true}}""", "The visual of an update target (visual, select.visual) must be an object.")]
    [TestCase(typeof(UpdateTarget), """{"elements":["Asia"]}""", "An item of an update target's elements must be an object.")]
    [TestCase(typeof(UpdateTarget), """{"select":{"where":{"region":["Asia"]}}}""", "A condition of an update target's select.where must be an object: {in} or {range}.")]
    [TestCase(typeof(TargetSelector), "true", "The select of an update target must be an object.")]
    [TestCase(typeof(TargetVisual), "\"mark\"", "The visual of an update target (visual, select.visual) must be an object.")]
    [TestCase(typeof(SemanticElement), "[]", "An item of an update target's elements must be an object.")]
    [TestCase(typeof(FilterValue), "\"Asia\"", "A condition of an update target's select.where must be an object: {in} or {range}.")]
    public void Deserialize_OtherShapes_ThrowJsonExceptionNamingTheMember(Type type, string json, string message)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize(json, type)), Throws.InstanceOf<JsonException>().With.Message.EqualTo(message));
    }

    // source: D:12-88 and D:487-495 (kind and role are strings; key, value and each record are objects; elements, records, in
    // and range are arrays); System.Text.Json names the member in the exception's path, relative to the object it reads
    [TestCase("""{"visual":{"kind":1}}""", "$.kind")]
    [TestCase("""{"visual":{"role":["bar"]}}""", "$.role")]
    [TestCase("""{"elements":{}}""", "$.elements")]
    [TestCase("""{"elements":[{"value":[]}]}""", "$.value")]
    [TestCase("""{"elements":[{"value":{},"records":{}}]}""", "$.records")]
    [TestCase("""{"elements":[{"records":[1]}]}""", "$.records[0]")]
    [TestCase("""{"select":{"key":[]}}""", "$.key")]
    [TestCase("""{"select":{"where":[]}}""", "$.where")]
    [TestCase("""{"select":{"where":{"region":{"in":"Asia"}}}}""", "$.in")]
    [TestCase("""{"select":{"where":{"month":{"range":{}}}}}""", "$.range")]
    public void Deserialize_MemberOfAnotherKind_ThrowsWithTheMemberPath(string json, string path)
    {
        Assert.That((Action)(() => JsonSerializer.Deserialize<UpdateTarget>(json)), Throws.InstanceOf<JsonException>().With.Property(nameof(JsonException.Path)).EqualTo(path));
        Assert.That((Action)(() => JsonSerializer.Deserialize<UpdateTarget>(json, HostileOptions.Create())), Throws.InstanceOf<JsonException>());
    }

    // source: the null value of a nullable member: a JSON null target is no target, as for every reference type
    [Test]
    public void Null_ReadsAsNull()
    {
        Assert.That(JsonSerializer.Deserialize<UpdateTarget>("null"), Is.Null);
        Assert.That(JsonSerializer.Deserialize<UpdateTarget[]>("[null]"), Is.EqualTo(new UpdateTarget?[] { null }));
    }
}
