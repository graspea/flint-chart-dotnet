// IR types (design §7.3) and the canonical writer: sorted keys, two-space indent, LF, trailing newline.

export type IrType =
  | { kind: 'string' }
  | { kind: 'number' }
  | { kind: 'boolean' }
  | { kind: 'any' }
  | { kind: 'unknown' }
  | { kind: 'never' }
  | { kind: 'literal'; value: string | number | boolean }
  | { kind: 'literalUnion'; values: string[] }
  | { kind: 'array'; element: IrType }
  | { kind: 'tuple'; elements: IrType[] }
  | { kind: 'record'; value: IrType }
  | { kind: 'object'; props: IrProp[] }
  | { kind: 'ref'; name: string }
  | { kind: 'partial'; of: IrType }
  | { kind: 'mapped'; text: string; props: IrProp[] }
  | { kind: 'intersection'; members: IrType[] }
  | { kind: 'union'; members: IrType[] }
  | { kind: 'unsupported'; text: string };

export interface IrProp {
  name: string;
  optional: boolean;
  type: IrType;
  doc?: string;
}

/** A named upstream type: an interface (object), an index-signature-only interface (record), or a core alias. */
export type IrNamedType = IrType & { doc?: string };

export type IrOptionValue = string | number | boolean | Array<string | number | boolean>;

export interface IrOption {
  label: string;
  value?: IrOptionValue;
  omitted?: true;
}

export interface IrProperty {
  key: string;
  label: string;
  type: 'binary' | 'discrete' | 'continuous';
  options?: IrOption[];
  min?: number;
  max?: number;
  step?: number;
}

export interface IrBackendTemplate {
  category?: string;
  channels: string[];
  properties: IrProperty[];
  typeMapping?: Record<string, string | boolean>;
  supportsFacets?: boolean;
  hasValidate?: boolean;
}

export interface IrChartType {
  name: string;
  backends: Record<string, IrBackendTemplate>;
}

export interface IrSemanticType {
  name: string;
  t0: string;
  t1: string;
  /** The candidate encodings (`getRegistryEntry(name).visEncodings`) upstream resolves an annotated field's type from. */
  visEncodings: string[];
  doc?: string;
}

export interface IrThemePreset {
  id: string;
  label: string;
  description: string;
}

export interface IrCatalog {
  backends: string[];
  backendsApplyingTheme: string[];
  hostStateKeys: string[];
  ignoredOptions: Record<string, string[]>;
  channels: string[];
  chartTypes: IrChartType[];
  semanticTypes: IrSemanticType[];
  themePresets: IrThemePreset[];
}

export interface Ir {
  upstream: { package: string; version: string; integrity: string };
  types: Record<string, IrNamedType>;
  catalog: IrCatalog;
}

/** Ordinal string comparison, independent of the host locale. */
export function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

function sortKeys(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortKeys);
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.keys(value)
        .sort(ordinal)
        .map((k) => [k, sortKeys((value as Record<string, unknown>)[k])]),
    );
  }
  return value;
}

/** Canonical JSON text: object keys sorted ordinally, arrays in order, two spaces, LF, trailing newline. */
export function canonicalJson(value: unknown): string {
  return JSON.stringify(sortKeys(value), null, 2) + '\n';
}
