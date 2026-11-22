using Graspea.Flint.Validation.Rules;

namespace Graspea.Flint.Tests.Validation;

/// <summary><c>Js.Round</c>, the validator's port of JavaScript's <c>Math.round</c> (compiled in from
/// src/Graspea.Flint/Validation/Rules/Js.cs), which the snapshot cadence of flint-chart 1.1.1 rounds with.</summary>
public sealed class JsRoundTests
{
    // source: ECMA-262 §21.3.2.28 Math.round (the integer closest to x, the one closer to +∞ on a tie; -0 for x in
    // [-0.5, 0); NaN and ±∞ as given), not .NET's Math.Round, which rounds a tie to even
    [TestCase(2.5, 3)]
    [TestCase(3.5, 4)]
    [TestCase(-2.5, -2)]
    [TestCase(-2.6, -3)]
    [TestCase(0.49999999999999994, 0)]
    [TestCase(4503599627370497d, 4503599627370497d)]
    [TestCase(1.4, 1)]
    [TestCase(double.PositiveInfinity, double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity, double.NegativeInfinity)]
    public void Round_IsMathRound(double x, double expected) => Assert.That(Js.Round(x), Is.EqualTo(expected));

    // source: ECMA-262 §21.3.2.28 (Math.round(-0.5) and Math.round(-0) are -0; Math.round(NaN) is NaN)
    [Test]
    public void Round_KeepsNegativeZeroAndNaN()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(double.IsNegative(Js.Round(-0.5)) && Js.Round(-0.5) == 0, Is.True, "-0.5 is -0");
            Assert.That(double.IsNegative(Js.Round(-0d)), Is.True, "-0 is -0");
            Assert.That(double.IsNaN(Js.Round(double.NaN)), Is.True, "NaN");
        }
    }
}
