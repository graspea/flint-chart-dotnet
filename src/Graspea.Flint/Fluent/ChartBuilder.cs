using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Graspea.Flint.Serialization;
using Graspea.Flint.Validation;

namespace Graspea.Flint;

/// <summary>
/// The core of every fluent builder: the raw setters the generated per-template methods delegate to, and
/// <see cref="Build"/>, <see cref="Validate"/> and <see cref="ToJson"/>. Builders are mutable and not thread-safe; each
/// method changes one member of the document and returns the builder. Channels and chart properties a template does not
/// declare are reachable here (<see cref="Encode(Channel, string, SemanticType?)"/>, <see cref="Property(string, JsonNode?)"/>).
/// </summary>
/// <typeparam name="TSelf">The concrete builder type, returned by every setter.</typeparam>
/// <typeparam name="TRow">The row type; selectors such as <c>r =&gt; r.Month</c> resolve to the key the rows are written
/// with.</typeparam>
public abstract class ChartBuilder<TSelf, TRow>
    where TSelf : ChartBuilder<TSelf, TRow>
{
    private readonly ChartType _chartType;
    private readonly ChartData? _data;
    private readonly JsonTypeInfo<TRow>? _rowTypeInfo;
    private readonly Dictionary<string, EncodingValue> _encodings = [];
    private readonly Dictionary<string, SemanticAnnotation> _semanticTypes = [];
    private readonly Dictionary<string, string> _displayNames = [];
    private readonly List<Action<AssembleOptions>> _options = [];
    private readonly List<Action<ChartAssemblyInput>> _configure = [];
    private Dictionary<string, JsonNode> _properties = [];
    private string? _title;
    private string? _subtitle;
    private ChartSize? _baseSize;
    private ChartSize? _canvasSize;
    private ChartTheme? _theme;

    internal ChartBuilder(ChartType chartType, ChartData? data, JsonTypeInfo<TRow>? rowTypeInfo)
    {
        _chartType = Chart.Require(chartType);
        _data = data;
        _rowTypeInfo = rowTypeInfo;
    }

    private TSelf Self => (TSelf)this;

    /// <summary>Binds a channel to a field: <c>{"field":…}</c>. The last call for a channel wins.</summary>
    /// <param name="channel">The channel, such as <see cref="Channel.X"/>.</param>
    /// <param name="field">The field: a key of the rows, used verbatim.</param>
    /// <param name="semanticType">When set, also annotates the field in <c>semantic_types</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="channel"/> or <paramref name="semanticType"/> is a default value.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public TSelf Encode(Channel channel, string field, SemanticType? semanticType = null)
    {
        var name = ChannelName(channel);
        ArgumentNullException.ThrowIfNull(field);
        if (semanticType is { } type)
        {
            Annotate(field, type);
        }

        _encodings[name] = EncodingValue.Single(new ChartEncoding { Field = field });
        return Self;
    }

    /// <summary>Binds a channel to the field a row member is written as: <c>r =&gt; r.Month</c> gives <c>month</c> under
    /// the default <see cref="RowOptions"/>, and <c>[JsonPropertyName]</c> is honoured.</summary>
    /// <param name="channel">The channel, such as <see cref="Channel.X"/>.</param>
    /// <param name="selector">A property or field of the row.</param>
    /// <param name="semanticType">When set, also annotates the field in <c>semantic_types</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
    /// <exception cref="ArgumentException">The selector is not a serialised member of the row, or an argument is a
    /// default value.</exception>
    /// <exception cref="InvalidOperationException">The data was entered as <see cref="ChartData"/>, so there is no row
    /// metadata.</exception>
    public TSelf Encode(Channel channel, Expression<Func<TRow, object?>> selector, SemanticType? semanticType = null)
    {
        ChannelName(channel);
        return Encode(channel, FieldName(selector), semanticType);
    }

    /// <summary>Binds a channel to an encoding with any of its members (<c>type</c>, <c>aggregate</c>, <c>sortOrder</c>,
    /// <c>sortBy</c>, <c>scheme</c>). The builder keeps a copy.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="encoding">The encoding.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is a default value.</exception>
    public TSelf Encode(Channel channel, ChartEncoding encoding)
    {
        var name = ChannelName(channel);
        ArgumentNullException.ThrowIfNull(encoding);
        _encodings[name] = EncodingValue.Single(Clone(encoding, FlintJsonContext.Default.ChartEncoding));
        return Self;
    }

    /// <summary>Sets a channel's <c>encodings</c> value as given: one encoding or a static series. The builder keeps a copy.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is a default value.</exception>
    public TSelf Encode(Channel channel, EncodingValue value)
    {
        var name = ChannelName(channel);
        ArgumentNullException.ThrowIfNull(value);
        _encodings[name] = Clone(value, FlintJsonContext.Default.EncodingValue);
        return Self;
    }

    /// <summary>A static series on <c>x</c> or <c>y</c>: the channel takes an array of fields, one series per field
    /// (<c>y: [{"field":"revenue"},{"field":"cost"}]</c>).</summary>
    /// <param name="channel"><see cref="Channel.X"/> or <see cref="Channel.Y"/>.</param>
    /// <param name="fields">At least two distinct fields.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    /// <exception cref="ArgumentException">The channel is not <c>x</c> or <c>y</c>, or there are fewer than two fields, a
    /// null or empty field or a duplicate; flint-chart rejects each of these on every backend.</exception>
    /// <remarks>A call with no fields or a literal <c>null</c> is ambiguous between the two overloads and does not compile
    /// (CS0121); pass at least two fields.</remarks>
    public TSelf Series(Channel channel, params string[] fields)
    {
        var name = ChannelName(channel);
        ArgumentNullException.ThrowIfNull(fields);
        if (name is not ("x" or "y"))
        {
            throw new ArgumentException($"A static series is only allowed on x or y, not \"{name}\".", nameof(channel));
        }

        if (fields.Length < 2 || fields.Any(string.IsNullOrEmpty) || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length)
        {
            throw new ArgumentException("A static series needs at least two distinct, non-empty fields.", nameof(fields));
        }

        _encodings[name] = EncodingValue.Series([.. fields.Select(f => new ChartEncoding { Field = f })]);
        return Self;
    }

    /// <summary>A static series on <c>x</c> or <c>y</c> from row members, resolved as in
    /// <see cref="Encode(Channel, Expression{Func{TRow, object}}, SemanticType?)"/>.</summary>
    /// <param name="channel"><see cref="Channel.X"/> or <see cref="Channel.Y"/>.</param>
    /// <param name="selectors">At least two distinct row members.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selectors"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">As for <see cref="Series(Channel, string[])"/>, or a selector is not a
    /// serialised member of the row.</exception>
    /// <exception cref="InvalidOperationException">The data was entered as <see cref="ChartData"/>.</exception>
    /// <remarks>A call with no fields or a literal <c>null</c> is ambiguous between the two overloads and does not compile
    /// (CS0121); pass at least two fields.</remarks>
    public TSelf Series(Channel channel, params Expression<Func<TRow, object?>>[] selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        return Series(channel, [.. selectors.Select(FieldName)]);
    }

    /// <summary>Changes the encoding bound to a channel (every entry of a static series), for members without a
    /// setter of their own: <c>aggregate</c>, <c>sortOrder</c>, <c>sortBy</c>, <c>scheme</c>, <c>type</c>. An unbound
    /// channel gets a new encoding without a field (<c>{"aggregate":"count"}</c>).</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="configure">Changes the encoding.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is a default value.</exception>
    /// <remarks>Use <c>Encode</c> to bind a field; use <c>Encoding</c> to change the bound encoding's aggregate, sort or
    /// scheme.</remarks>
    public TSelf Encoding(Channel channel, Action<ChartEncoding> configure)
    {
        var name = ChannelName(channel);
        ArgumentNullException.ThrowIfNull(configure);
        if (!_encodings.TryGetValue(name, out var value))
        {
            value = _encodings[name] = EncodingValue.Single(new ChartEncoding());
        }

        foreach (var encoding in value.Encodings)
        {
            configure(encoding);
        }

        return Self;
    }

    /// <summary>Sets a <c>chartProperties</c> key; null removes it. The builder keeps a copy of the node.</summary>
    /// <param name="key">The property key, verbatim (such as <c>logScale_x</c>).</param>
    /// <param name="value">The value; null removes the key.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public TSelf Property(string key, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (value is not null)
        {
            _properties[key] = value.DeepClone();
        }
        else if (_properties.ContainsKey(key))
        {
            // Rebuilt rather than removed in place, so a key set later is written after the others.
            _properties = _properties.Where(p => p.Key != key).ToDictionary(p => p.Key, p => p.Value);
        }

        return Self;
    }

    /// <summary>Sets a <c>chartProperties</c> key to a string; null removes it (<c>Property(key, null)</c> binds here).</summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value; null removes the key.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public TSelf Property(string key, string? value) => Property(key, value is null ? null : JsonValue.Create(value));

    /// <summary>Sets a <c>chartProperties</c> key to a boolean.</summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public TSelf Property(string key, bool value) => Property(key, (JsonNode)JsonValue.Create(value));

    /// <summary>Sets a <c>chartProperties</c> key to a number.</summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public TSelf Property(string key, double value) => Property(key, (JsonNode)JsonValue.Create(value));

    /// <summary>Sets <c>chart_spec.title</c>; null removes it.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public TSelf Title(string? title)
    {
        _title = title;
        return Self;
    }

    /// <summary>Sets <c>chart_spec.subtitle</c>; null removes it.</summary>
    /// <param name="subtitle">The subtitle.</param>
    /// <returns>This builder.</returns>
    public TSelf Subtitle(string? subtitle)
    {
        _subtitle = subtitle;
        return Self;
    }

    /// <summary>Sets <c>chart_spec.baseSize</c>, the size of the plot area before layout stretching.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>This builder.</returns>
    public TSelf BaseSize(double width, double height)
    {
        _baseSize = new ChartSize(width, height);
        return Self;
    }

    /// <summary>Sets <c>chart_spec.canvasSize</c>, the size of the whole canvas.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>This builder.</returns>
    public TSelf CanvasSize(double width, double height)
    {
        _canvasSize = new ChartSize(width, height);
        return Self;
    }

    /// <summary>Annotates a field in <c>semantic_types</c>; a <see cref="SemanticType"/> converts to an annotation.
    /// The builder keeps a copy.</summary>
    /// <param name="field">The field, verbatim.</param>
    /// <param name="annotation">The annotation.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> or <paramref name="annotation"/> is null.</exception>
    /// <exception cref="ArgumentException">The annotation's type is <c>default(SemanticType)</c>.</exception>
    public TSelf Annotate(string field, SemanticAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(annotation);
        if (annotation.Type.Name is null)
        {
            throw new ArgumentException("default(SemanticType) is not a semantic type.", nameof(annotation));
        }

        _semanticTypes[field] = Clone(annotation, FlintJsonContext.Default.SemanticAnnotation);
        return Self;
    }

    /// <summary>Annotates the field a row member is written as.</summary>
    /// <param name="selector">A property or field of the row.</param>
    /// <param name="annotation">The annotation.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The selector is not a serialised member of the row.</exception>
    /// <exception cref="InvalidOperationException">The data was entered as <see cref="ChartData"/>.</exception>
    public TSelf Annotate(Expression<Func<TRow, object?>> selector, SemanticAnnotation annotation) => Annotate(FieldName(selector), annotation);

    /// <summary>Sets the label shown for a field (<c>field_display_names</c>).</summary>
    /// <param name="field">The field, verbatim.</param>
    /// <param name="displayName">The label.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public TSelf DisplayName(string field, string displayName)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(displayName);
        _displayNames[field] = displayName;
        return Self;
    }

    /// <summary>Sets the label shown for the field a row member is written as.</summary>
    /// <param name="selector">A property or field of the row.</param>
    /// <param name="displayName">The label.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The selector is not a serialised member of the row.</exception>
    /// <exception cref="InvalidOperationException">The data was entered as <see cref="ChartData"/>.</exception>
    public TSelf DisplayName(Expression<Func<TRow, object?>> selector, string displayName) => DisplayName(FieldName(selector), displayName);

    /// <summary>Sets <c>theme_spec</c>: a <see cref="ThemePreset"/> or a <see cref="Theming.ThemeSpec"/>. Vega-Lite and
    /// Plotly apply it; the other backends ignore it.</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is null.</exception>
    public TSelf Theme(ChartTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        return Self;
    }

    /// <summary>Changes <c>options</c>; every call is applied, in order, to a new <see cref="AssembleOptions"/> at
    /// <see cref="Build"/>. Objects the action assigns are used as given, not copied.</summary>
    /// <param name="configure">Changes the options.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    public TSelf Options(Action<AssembleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _options.Add(configure);
        return Self;
    }

    /// <summary>Changes the built document directly; every call is applied, in order, after everything else at
    /// <see cref="Build"/>. Objects the action assigns are used as given, not copied.</summary>
    /// <param name="configure">Changes the document.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
    public TSelf Configure(Action<ChartAssemblyInput> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure.Add(configure);
        return Self;
    }

    /// <summary>Builds the document. Every call returns a new document: state the builder holds is copied for each
    /// document; objects that an <see cref="Options"/> or <see cref="Configure"/> action assigns are used as given. Never
    /// validates; call <see cref="Validate"/> for that. A builder from a deferred entry (<c>Chart.Line(typeInfo)</c>)
    /// returns a document without data.</summary>
    /// <returns>The document.</returns>
    public ChartAssemblyInput Build() => Create(copyRows: true);

    /// <summary>Validates the document <see cref="Build"/> would return; the only validating method of the builder. A
    /// document without data reports <c>data_missing</c>; validate a spec whose rows are bound later with
    /// <c>ToTemplate().Validate(backend)</c>.</summary>
    /// <param name="backend">The target backend; null means every backend that has the template.</param>
    /// <returns>The issues (<see cref="ChartValidator.Validate"/>).</returns>
    public ValidationResult Validate(FlintBackend? backend = null) => ChartValidator.Validate(Create(copyRows: _configure.Count > 0), backend);

    /// <summary>The document <see cref="Build"/> would return, as JSON (<see cref="FlintJson.Serialize(ChartAssemblyInput, bool)"/>).
    /// Never validates.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => FlintJson.Serialize(Create(copyRows: _configure.Count > 0));

    /// <summary>A reusable, thread-safe template of the document <see cref="Build"/> would return, without its data: the
    /// <see cref="Options"/> and <see cref="Configure"/> actions run once, now, and later changes to this builder do not
    /// reach it. Bind rows per request with <see cref="ChartTemplate{TRow}.Bind(IEnumerable{TRow})"/>,
    /// <see cref="ChartTemplate{TRow}.BindAsync"/> or <see cref="ChartTemplate{TRow}.BindStream(IAsyncEnumerable{TRow})"/>.</summary>
    /// <returns>The template; it writes rows with the metadata of the entry point (none for data entered as
    /// <see cref="ChartData"/>).</returns>
    public ChartTemplate<TRow> ToTemplate() => new(Create(copyRows: _configure.Count > 0), _rowTypeInfo);

    /// <summary>The document; the rows are copied only when the caller may change them (a <see cref="Configure"/> action
    /// or <see cref="Build"/>).</summary>
    private ChartAssemblyInput Create(bool copyRows)
    {
        AssembleOptions? options = null;
        if (_options.Count > 0)
        {
            options = new AssembleOptions();
            foreach (var configure in _options)
            {
                configure(options);
            }
        }

        var input = new ChartAssemblyInput
        {
            Data = copyRows ? CopyData(_data) : _data,
            SemanticTypes = _semanticTypes.Count == 0 ? null : _semanticTypes.ToDictionary(p => p.Key, p => Clone(p.Value, FlintJsonContext.Default.SemanticAnnotation)),
            ChartSpec = new ChartSpec
            {
                ChartType = _chartType,
                Title = _title,
                Subtitle = _subtitle,
                Encodings = _encodings.ToDictionary(p => p.Key, p => Clone(p.Value, FlintJsonContext.Default.EncodingValue)),
                BaseSize = _baseSize,
                CanvasSize = _canvasSize,
                ChartProperties = _properties.Count == 0 ? null : _properties.ToDictionary(p => p.Key, p => (JsonNode?)p.Value.DeepClone()),
            },
            ThemeSpec = _theme is null ? null : Clone(_theme, FlintJsonContext.Default.ChartTheme),
            Options = options,
            FieldDisplayNames = _displayNames.Count == 0 ? null : new Dictionary<string, string>(_displayNames),
        };
        foreach (var configure in _configure)
        {
            configure(input);
        }

        return input;
    }

    private static ChartData? CopyData(ChartData? data) =>
        data is null ? null : ChartData.Create(data.Values?.DeepClone().AsArray(), data.Url, data.Extensions is null ? null : new(data.Extensions));

    /// <summary>A deep copy through the library's own metadata, so builder state and built documents never share an object.</summary>
    private static T Clone<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), typeInfo)!;

    private string FieldName(Expression<Func<TRow, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var typeInfo = _rowTypeInfo ?? throw new InvalidOperationException(
            "The data was entered as ChartData, so there is no row metadata to resolve a selector; pass the field name as a string.");
        return RowMaterializer.FieldName(typeInfo, selector);
    }

    private static string ChannelName(Channel channel) =>
        channel.Name ?? throw new ArgumentException("default(Channel) is not a channel.", nameof(channel));
}
