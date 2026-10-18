using System.Text.Json;

namespace Graspea.Flint.Tests.Model;

public sealed class SortByTests
{
    // source: design §2 (sortBy: x | y | color or a JSON-encoded array string) and §4.1 (SortBy.ExplicitOrder)
    [Test]
    public void ExplicitOrder_WritesACompactJsonArrayString()
    {
        Assert.That(SortBy.ExplicitOrder("Jan", "Feb"), Is.EqualTo("""["Jan","Feb"]"""));
    }

    // source: RFC 8259 §7 (a quote inside a string is escaped), so JSON.parse reads the values back unchanged
    [Test]
    public void ExplicitOrder_ValuesWithQuotesAndNonAscii_ParseBackUnchanged()
    {
        string[] order = ["a\"b", "Čt", "x<y"];
        Assert.That(JsonSerializer.Deserialize<string[]>(SortBy.ExplicitOrder(order)), Is.EqualTo(order));
    }

    // source: design §4.1 (a null argument is an argument error)
    [Test]
    public void ExplicitOrder_Null_ThrowsArgumentNullException()
    {
        Assert.That((Action)(() => SortBy.ExplicitOrder(null!)), Throws.ArgumentNullException);
    }
}
