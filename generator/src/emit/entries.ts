// entries emitter (design §4.2, §7.4, §13.3.2): per chart type the six Chart.{Entry} overloads (rows with RowOptions by
// reflection, rows with a JsonTypeInfo, the two deferred entries without rows, ChartData, and the error-marked string
// overload), each a one-line delegation.
import type { Ir } from '../ir.ts';
import type { Overrides } from '../overrides.ts';
import { csString, docComment, emptyResult, header, xmlText, type EmitResult } from './common.ts';
import { fluentModel } from './fluent.ts';

const INDENT = '    ';
/** The JsonTypeInfo overload bypasses the library's enum and non-finite converters (review R1-1). */
export const TYPE_INFO_REMARKS =
  'Values are written exactly as this metadata writes them, unlike the reflection overload: enums as numbers unless the context sets `UseStringEnumConverter = true`, and a `NaN` or infinite `double` throws unless the row maps it to `null`. Recommended: `[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]`.';

const tag = (name: string, attribute: string, text: string) => `${INDENT}/// <${name}${attribute}>${xmlText(text)}</${name}>\n`;

export function emitEntries(ir: Ir, overrides: Overrides): EmitResult {
  const result = emptyResult();
  for (const t of fluentModel(ir, overrides)) {
    const b = t.builder;
    const e = t.entry;
    const typeParam = tag('typeparam', ' name="TRow"', 'The row type.');
    const rows = tag('param', ' name="rows"', 'The rows; written to JSON now.');
    const returns = tag('returns', '', 'The builder.');
    const optionsParam = tag('param', ' name="options"', 'Key naming; `RowOptions.Default` when omitted. A literal `null` here does not compile (ambiguous with the `JsonTypeInfo` overload); omit the argument.');
    const typeInfoParam = tag('param', ' name="typeInfo"', 'A `JsonTypeInfo<TRow>`, typically from a source-generated `JsonSerializerContext`, whose null members are written rather than omitted (trimming- and AOT-safe).');
    const reflection = `${INDENT}[RequiresUnreferencedCode(RowMaterializer.ReflectionMessage)]\n${INDENT}[RequiresDynamicCode(RowMaterializer.ReflectionMessage)]\n`;
    const content =
      `${header(ir)}\nusing System.Diagnostics.CodeAnalysis;\nusing System.Text.Json.Serialization.Metadata;\nusing Graspea.Flint.Serialization;\n\n` +
      'namespace Graspea.Flint;\n\npublic static partial class Chart\n{\n' +
      docComment(INDENT, `Starts a \`${t.name}\` document from rows written by reflection (camelCase keys by default).`) +
      typeParam +
      rows +
      optionsParam +
      returns +
      reflection +
      `${INDENT}public static ${b}<TRow> ${e}<TRow>(IEnumerable<TRow> rows, RowOptions? options = null) => new(ChartRows.Of(rows, options));\n\n` +
      docComment(INDENT, `Starts a \`${t.name}\` document from rows written through source-generated or custom metadata (trimming- and AOT-safe).`) +
      typeParam +
      rows +
      typeInfoParam +
      returns +
      tag('remarks', '', TYPE_INFO_REMARKS) +
      `${INDENT}public static ${b}<TRow> ${e}<TRow>(IEnumerable<TRow> rows, JsonTypeInfo<TRow> typeInfo) => new(ChartRows.Of(rows, typeInfo));\n\n` +
      docComment(INDENT, `Starts a \`${t.name}\` document without rows, written later by reflection (camelCase keys by default): bind them through \`ToTemplate()\`.`) +
      typeParam +
      optionsParam +
      returns +
      reflection +
      `${INDENT}public static ${b}<TRow> ${e}<TRow>(RowOptions? options = null) => new(ChartRows.Deferred<TRow>(options));\n\n` +
      docComment(INDENT, `Starts a \`${t.name}\` document without rows, written later through source-generated or custom metadata (trimming- and AOT-safe): bind them through \`ToTemplate()\`.`) +
      typeParam +
      typeInfoParam +
      returns +
      tag('remarks', '', TYPE_INFO_REMARKS) +
      `${INDENT}public static ${b}<TRow> ${e}<TRow>(JsonTypeInfo<TRow> typeInfo) => new(ChartRows.Deferred(typeInfo));\n\n` +
      docComment(INDENT, `Starts a \`${t.name}\` document over data that is already built (JSON rows or a URL); bind fields by name.`) +
      tag('param', ' name="data"', 'The data.') +
      returns +
      `${INDENT}public static ${b}<object> ${e}(ChartData data) => new(ChartRows.Of(data));\n\n` +
      docComment(INDENT, `Not row data: a string is \`IEnumerable<char>\`. Use \`Chart.${e}(ChartData.FromUrl(url))\`.`) +
      tag('param', ' name="url"', 'The URL.') +
      tag('returns', '', 'Never returns.') +
      `${INDENT}[Obsolete(${csString(`A string is IEnumerable<char>; use Chart.${e}(ChartData.FromUrl(url)).`)}, error: true)]\n` +
      `${INDENT}public static ${b}<object> ${e}(string url) => throw ChartRows.UrlIsNotRows();\n}\n`;
    result.files.push({ path: `Entries/Chart.${e}.g.cs`, content });
    const self = `Graspea.Flint.${b}`;
    result.apiLines.push(
      `static Graspea.Flint.Chart.${e}<TRow>(System.Collections.Generic.IEnumerable<TRow>! rows, Graspea.Flint.RowOptions? options = null) -> ${self}<TRow>!`,
      `static Graspea.Flint.Chart.${e}<TRow>(System.Collections.Generic.IEnumerable<TRow>! rows, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRow>! typeInfo) -> ${self}<TRow>!`,
      `static Graspea.Flint.Chart.${e}<TRow>(Graspea.Flint.RowOptions? options = null) -> ${self}<TRow>!`,
      `static Graspea.Flint.Chart.${e}<TRow>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRow>! typeInfo) -> ${self}<TRow>!`,
      `static Graspea.Flint.Chart.${e}(Graspea.Flint.ChartData! data) -> ${self}<object!>!`,
      `static Graspea.Flint.Chart.${e}(string! url) -> ${self}<object!>!`,
    );
    result.ownedMembers.push(`Graspea.Flint.Chart.${e}`);
  }
  return result;
}
