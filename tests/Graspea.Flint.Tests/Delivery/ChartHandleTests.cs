using System.Text.Json;
using Graspea.Flint.Delivery;

namespace Graspea.Flint.Tests.Delivery;

public sealed class ChartHandleTests
{
    private static ChartTemplate<Sale> Template() =>
        Chart.Line(DeliveryJson.Default.Sale).X(r => r.Month).Y(r => r.Revenue).ToTemplate();

    // source: design §14.5 rule 3 ([A-Za-z0-9_.-]+; __proto__ is legal)
    [TestCase("a")]
    [TestCase("Revenue-by.month_2")]
    [TestCase("__proto__")]
    public void Id_InsideTheAlphabet_IsAccepted(string id) =>
        Assert.That(new ChartHandle<Sale>(id, Template()).Id, Is.EqualTo(id));

    // source: §14.5 rule 3, §14.4.2 Errors row
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("a b")]
    [TestCase("é")]
    [TestCase("a/b")]
    [TestCase("a\n")]
    public void Id_OutsideTheAlphabet_Throws(string id)
    {
        Assert.That(() => new ChartHandle<Sale>(id, Template()), Throws.ArgumentException);
    }

    // source: §14.5 rule 3, §14.4.2 Errors row
    [Test]
    public void Id_Null_Throws() =>
        Assert.That(() => new ChartHandle<Sale>(null!, Template()), Throws.ArgumentNullException);

    // source: §14.4.1 (a template without row metadata is refused at the handle; null metadata, not
    // typeof(object), is the signal, see ChartHandleOfTRow.cs:23)
    [Test]
    public void TemplateWithoutRowMetadata_Throws()
    {
        var bare = Chart.Line(ChartData.FromUrl("https://example.org/rows.json")).X("month").ToTemplate();
        Assert.That(() => new ChartHandle<object>("a", bare), Throws.ArgumentException.With.Message.Contains("no row metadata"));

        var spec = Chart.Line(ChartData.FromUrl("https://example.org/rows.json")).X("month").Build();
        var withMetadata = ChartTemplate.Create(spec, DeliveryJson.Default.Object);
        Assert.That(new ChartHandle<object>("a", withMetadata).Template, Is.SameAs(withMetadata));
    }

    // source: §14.4.2 Errors row (null template)
    [Test]
    public void Template_Null_Throws() =>
        Assert.That(() => new ChartHandle<Sale>("a", null!), Throws.ArgumentNullException);

    // source: §14.11 defaults (maxRows at least 1); §14.4.2 MaxRows row
    [Test]
    public void MaxRows_BelowOne_Throws()
    {
        Assert.That(() => new ChartHandle<Sale>("a", Template(), 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new ChartHandle<Sale>("a", Template(), -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(new ChartHandle<Sale>("a", Template()).MaxRows, Is.Null);
        Assert.That(new ChartHandle<Sale>("a", Template(), 1).MaxRows, Is.EqualTo(1));
    }

    // source: §14.4.1 (non-empty host-defined code, written as given)
    [Test]
    public void Withheld()
    {
        var handle = new ChartHandle<Sale>("a", Template());
        var part = handle.Withheld("restricted");
        Assert.That(part.WithheldCode, Is.EqualTo("restricted"));
        Assert.That(part.IsStreamed, Is.False);
        Assert.That(part.Chart, Is.SameAs(handle));
        Assert.That(() => handle.Withheld(""), Throws.ArgumentException);
        Assert.That(() => handle.Withheld(null!), Throws.ArgumentNullException);
    }

    // source: §14.4.1 data-free template (a handle needs a template without a data member)
    [Test]
    public void TemplateWithDataMember_Throws()
    {
        var template = Chart.Line(DeliveryJson.Default.Sale)
            .X(r => r.Month)
            .Y(r => r.Revenue)
            .Configure(input => input.Extensions = new() { ["data"] = JsonDocument.Parse("{}").RootElement })
            .ToTemplate();
        Assert.That(() => new ChartHandle<Sale>("a", template),
            Throws.ArgumentException.With.Message.Contains("has a data member"));
    }
}
