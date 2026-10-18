namespace Graspea.Flint.CSharp;

/// <summary>
/// FLINT103 (design §12.6.7): whether a JSON value is a <c>ChartAssemblyInput</c>, decided as <c>FlintJson.Deserialize</c>
/// decides it: System.Text.Json over the typed model, with <c>chart_spec</c> and <c>chartType</c> required, string-backed
/// values (chart types, semantic types, presets, literals) non-empty strings, strict numbers (any JSON number, read as a
/// <c>double</c>, an overflow as infinity), and unknown keys passed through. The option and theme classes are checked
/// through the name tables. A test compares the verdict with the library's over crafted shapes and over every node of a
/// corpus sample replaced by other values.
/// </summary>
internal static class SpecShape
{
    private const string Literal = "literal";
    private const string RequiredLiteral = "literal!";
    private const string RequiredDouble = "double!";

    /// <summary>The first reason the library would refuse the document, or null when it reads it.</summary>
    public static SpecIssue? Check(JsonItem root)
    {
        try
        {
            Document(root);
            return null;
        }
        catch (ShapeException e)
        {
            return new SpecIssue("FLINT103", "not_a_chart_assembly_input", SpecSeverity.Error, e.Path, e.Node.Start, e.Node.End,
                $"The JSON is not a ChartAssemblyInput: {e.Message} FlintJson.Deserialize refuses it.");
        }
    }

    private static void Document(JsonItem root)
    {
        if (!root.IsObject)
        {
            throw new ShapeException(root, string.Empty, "the document must be a JSON object.");
        }

        if (root.Get("chart_spec") is null)
        {
            throw new ShapeException(root, string.Empty, "chart_spec is required.");
        }

        foreach (var member in root.Members)
        {
            var value = member.Value;
            var path = member.Key;
            switch (member.Key)
            {
                case "data":
                    Data(value, path);
                    break;
                case "semantic_types":
                    Map(value, path, Annotation);
                    break;
                case "chart_spec":
                    ChartSpec(value, path);
                    break;
                case "theme_spec":
                    Theme(value, path);
                    break;
                case "options":
                    Typed(value, "Graspea.Flint.AssembleOptions", path);
                    break;
                case "field_display_names":
                    Typed(value, "Dictionary<string, string>", path);
                    break;
            }
        }
    }

    private static void Data(JsonItem value, string path)
    {
        if (value.IsNull)
        {
            return;
        }

        Require(value.IsObject, value, path, "data must be an object.");
        foreach (var member in value.Members)
        {
            var at = $"{path}.{member.Key}";
            switch (member.Key)
            {
                case "values":
                    Require(member.Value.IsArray || member.Value.IsNull, member.Value, at, "data.values must be an array.");
                    break;
                case "url":
                    Typed(member.Value, "string", at);
                    break;
            }
        }
    }

    private static void ChartSpec(JsonItem value, string path)
    {
        if (value.IsNull)
        {
            return;
        }

        Require(value.IsObject, value, path, "chart_spec must be an object.");
        if (value.Get("chartType") is null)
        {
            throw new ShapeException(value, path, "chart_spec.chartType is required.");
        }

        foreach (var member in value.Members)
        {
            var at = $"{path}.{member.Key}";
            switch (member.Key)
            {
                case "chartType":
                    Typed(member.Value, RequiredLiteral, at);
                    break;
                case "title" or "subtitle":
                    Typed(member.Value, "string", at);
                    break;
                case "encodings":
                    Map(member.Value, at, Encoding);
                    break;
                case "baseSize" or "canvasSize":
                    Typed(member.Value, "Graspea.Flint.ChartSize", at);
                    break;
                case "chartProperties":
                    Typed(member.Value, "Dictionary<string, JsonNode?>", at);
                    break;
            }
        }
    }

    private static void Encoding(JsonItem value, string path)
    {
        if (value.IsNull || value.IsString)
        {
            return;
        }

        if (value.IsObject)
        {
            ChartEncoding(value, path);
            return;
        }

        Require(value.IsArray, value, path, "an encoding must be a string, an object or an array of strings and objects.");
        for (var i = 0; i < value.Items.Count; i++)
        {
            var entry = value.Items[i];
            var at = $"{path}[{i}]";
            Require(entry.IsString || entry.IsObject, entry, at, "an encoding must be a string, an object or an array of strings and objects.");
            if (entry.IsObject)
            {
                ChartEncoding(entry, at);
            }
        }
    }

    private static void ChartEncoding(JsonItem value, string path)
    {
        foreach (var member in value.Members)
        {
            var type = member.Key switch
            {
                "field" or "sortBy" or "scheme" => "string",
                "type" or "aggregate" or "sortOrder" => Literal,
                _ => null,
            };
            if (type is not null)
            {
                Typed(member.Value, type, $"{path}.{member.Key}");
            }
        }
    }

    private static void Annotation(JsonItem value, string path)
    {
        if (value.IsNull || value.IsString)
        {
            Typed(value, Literal, path);
            return;
        }

        Require(value.IsObject, value, path, "an annotation must be a semantic type name or an object.");
        if (value.Get("semanticType") is null)
        {
            throw new ShapeException(value, path, "an annotation object needs semanticType.");
        }

        foreach (var member in value.Members)
        {
            var type = member.Key switch
            {
                "semanticType" => RequiredLiteral,
                "intrinsicDomain" => "double[]",
                "unit" => "string",
                "divergingMidpoint" => "double",
                "sortOrder" => "string[]",
                _ => null,
            };
            if (type is not null)
            {
                Typed(member.Value, type, $"{path}.{member.Key}");
            }
        }
    }

    private static void Theme(JsonItem value, string path)
    {
        if (value.IsNull || value.IsString)
        {
            Typed(value, Literal, path);
            return;
        }

        Require(value.IsObject, value, path, "theme_spec must be a preset id or an object.");
        Typed(value, "Graspea.Flint.Theming.ThemeSpec", path);
    }

    private static void Map(JsonItem value, string path, Action<JsonItem, string> entry)
    {
        if (value.IsNull)
        {
            return;
        }

        Require(value.IsObject, value, path, $"{path} must be an object.");
        foreach (var member in value.Members)
        {
            entry(member.Value, $"{path}.{member.Key}");
        }
    }

    /// <summary>A value of a model or name-table type: <c>string</c>, <c>double</c>, <c>bool</c>, <c>JsonNode</c>, arrays,
    /// string-keyed maps, <c>ChartSize</c>, <c>ContinuousMarkCrossSection</c>, a string-backed literal or a generated
    /// class. Every type is nullable except the <c>!</c> forms.</summary>
    private static void Typed(JsonItem value, string type, string path)
    {
        if (type is "JsonNode" or "JsonNode?")
        {
            return;
        }

        if (value.IsNull)
        {
            Require(type is not (RequiredLiteral or RequiredDouble), value, path, $"{path} must not be null.");
            return;
        }

        switch (type)
        {
            case "string":
                Require(value.IsString, value, path, $"{path} must be a string.");
                return;
            case "double" or RequiredDouble:
                Require(value.IsNumber, value, path, $"{path} must be a number.");
                return;
            case "bool":
                Require(value.IsBoolean, value, path, $"{path} must be true or false.");
                return;
            case Literal or RequiredLiteral:
                Require(value.IsString && value.Value!.Length > 0, value, path, $"{path} must be a non-empty string.");
                return;
            case "Graspea.Flint.ChartSize":
                Require(value.IsObject, value, path, $"{path} must be an object with width and height.");
                foreach (var member in value.Members.Where(m => m.Key is "width" or "height"))
                {
                    Typed(member.Value, RequiredDouble, $"{path}.{member.Key}");
                }

                return;
            case "Graspea.Flint.ContinuousMarkCrossSection":
                CrossSection(value, path);
                return;
        }

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            Require(value.IsArray, value, path, $"{path} must be an array.");
            var element = type.Substring(0, type.Length - 2);
            for (var i = 0; i < value.Items.Count; i++)
            {
                Typed(value.Items[i], element == "double" ? RequiredDouble : element, $"{path}[{i}]");
            }

            return;
        }

        const string map = "Dictionary<string, ";
        if (type.StartsWith(map, StringComparison.Ordinal))
        {
            var valueType = type.Substring(map.Length, type.Length - map.Length - 1);
            Map(value, path, (entry, at) => Typed(entry, valueType, at));
            return;
        }

        var names = NameTables.Class(type) ?? throw new InvalidOperationException($"No shape for the type {type}.");
        Require(value.IsObject, value, path, $"{path} must be an object.");
        foreach (var member in value.Members)
        {
            if (Array.Find(names.Members, m => m.Wire == member.Key) is { } declared)
            {
                Typed(member.Value, declared.Type, $"{path}.{member.Key}");
            }
        }
    }

    private static void CrossSection(JsonItem value, string path)
    {
        if (value.IsNumber)
        {
            Typed(value, "double", path);
            return;
        }

        Require(value.IsObject, value, path, $"{path} must be a number or an object.");
        foreach (var member in value.Members)
        {
            var type = member.Key switch
            {
                "x" or "y" or "elasticity" or "maxStretch" => "double",
                "seriesCountAxis" => Literal,
                _ => null,
            };
            if (type is not null)
            {
                Typed(member.Value, type, $"{path}.{member.Key}");
            }
        }
    }

    private static void Require(bool condition, JsonItem node, string path, string message)
    {
        if (!condition)
        {
            throw new ShapeException(node, path, message);
        }
    }

    private sealed class ShapeException(JsonItem node, string path, string message) : Exception(message)
    {
        public JsonItem Node { get; } = node;

        public string Path { get; } = path;
    }
}
