namespace Graspea.Flint;

public sealed partial class ChartAssemblyInput
{
    /// <summary>A shallow copy without <see cref="Data"/>: the members after <c>data</c>, written once by the streamed
    /// writer and the templates without touching the caller's object.</summary>
    internal ChartAssemblyInput WithoutData()
    {
        var copy = (ChartAssemblyInput)MemberwiseClone();
        copy.Data = null;
        return copy;
    }
}
