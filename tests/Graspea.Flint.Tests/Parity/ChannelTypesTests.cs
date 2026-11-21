using Graspea.Flint.Serialization;
using Graspea.Flint.Tests.Support;
using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint.Tests.Parity;

/// <summary>
/// The encoding type the validator resolves for a channel (<c>FieldTypes.Of</c>, compiled in from
/// src/Graspea.Flint/Validation/Rules/), against what <c>resolveChannelSemantics</c> of the pinned flint-chart gave the
/// generator's channel-type probes (tests/fixtures/flint-chart@&lt;pin&gt;/probes.json <c>channelTypes</c>, each over its
/// document in <c>channelTypeInputs</c>; generator/src/probes.ts): snapshots on a banded axis, the spellings a semantic
/// type rewrites and the strings untyped inference reads as dates. A probe whose answer needs V8's <c>Date.parse</c> may
/// be undecided, and only one listed in <see cref="UndecidedHere"/>.
/// </summary>
public sealed class ChannelTypesTests
{
    private static Dictionary<string, string> Recorded =>
        CommittedIr.Probes["channelTypes"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>(), StringComparer.Ordinal);

    /// <summary>The probes the validator leaves undecided, because their values are strings only V8's legacy parser reads.</summary>
    private static readonly Dictionary<string, string> UndecidedHere = new(StringComparer.Ordinal)
    {
        // source: src/core/decisions.ts:172-213 of flint-chart 1.0.1 (validateTemporalParsing reads each value through
        // Date.parse; none of these strings is an ISO date, so only V8's legacy parser reads them, ECMA-262 §21.4.3.2)
        ["line x, Date spellings with an invalid day"] = "2025/02/30, a slash date with a day past the month's end",
        ["line x, untyped \"2025/01/15\""] = "2025/01/15, a slash date",
        ["line x, untyped \"Jan 15, 2025\""] = "Jan 15, 2025, a month name",
    };

    public static IEnumerable<TestCaseData> Probes() =>
        CommittedIr.Probes["channelTypes"]!.AsObject().Select(p => new TestCaseData(p.Key).SetName($"ProbedChannelType_MatchesTheValidator({p.Key.Replace('"', '\'')})"));

    private static FieldType Resolve(string probe)
    {
        var source = CommittedIr.Probes["channelTypeInputs"]![probe]!;
        var input = FlintJson.Deserialize(source["input"]!.ToJsonString());
        return new FieldTypes(input).Of(source["channel"]!.GetValue<string>());
    }

    // source: probes.json channelTypes (an "ordinal snapshots" answer is ordinal; FieldType.TemporalOrOrdinal is the
    // validator's undecided answer between the two)
    [TestCaseSource(nameof(Probes))]
    public void ProbedChannelType_MatchesTheValidator(string probe)
    {
        var expected = Recorded[probe] switch
        {
            "temporal" => FieldType.Temporal,
            "ordinal" or "ordinal snapshots" => FieldType.Ordinal,
            "nominal" => FieldType.Nominal,
            "quantitative" => FieldType.Quantitative,
            var other => throw new InvalidOperationException($"{probe}: no FieldType stands for \"{other}\""),
        };
        var actual = Resolve(probe);
        if (UndecidedHere.ContainsKey(probe) && actual is FieldType.TemporalOrOrdinal or FieldType.Unknown)
        {
            // An undecided answer leaves the recorded one open: TemporalOrOrdinal leaves temporal and ordinal.
            if (actual == FieldType.TemporalOrOrdinal)
            {
                Assert.That(expected, Is.AnyOf(FieldType.Temporal, FieldType.Ordinal), $"{probe}: undecided between temporal and ordinal");
            }

            return;
        }

        Assert.That(actual, Is.EqualTo(expected), $"{probe}: upstream reads \"{Recorded[probe]}\"");
    }

    // source: UndecidedHere (each entry is still undecided, so the list names no probe the validator now decides)
    [Test]
    public void UndecidedProbes_AreStillUndecided()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var probe in UndecidedHere.Keys)
            {
                Assert.That(Recorded, Contains.Key(probe));
                Assert.That(Resolve(probe), Is.AnyOf(FieldType.TemporalOrOrdinal, FieldType.Unknown), probe);
            }
        }
    }
}
