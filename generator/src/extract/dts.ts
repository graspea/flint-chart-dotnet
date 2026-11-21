// flint-chart d.ts -> IR.types through the TypeScript checker (design §7.1). The entry point is package.json
// "types"; exports are followed by the checker, hashed declaration files are never globbed.
import { join, relative } from 'node:path';
import ts from 'typescript';
import { ordinal, type IrNamedType, type IrProp, type IrType } from '../ir.ts';
import { packageManifest } from './package.ts';

/** Exported upstream types the IR starts from: emitted (AssembleOptions, ThemeSpec) or parity-only. */
export const ROOT_TYPES = ['AssembleOptions', 'ThemeSpec', 'ChartAssemblyInput', 'ChartEncoding', 'SemanticAnnotation'] as const;

/**
 * Types of the `flint-chart/interactive` entry point (package.json `exports["./interactive"].types`): the option shape of
 * every interaction preset, keyed by its type name. Its members are the types the presets' factories take.
 */
export const INTERACTIVE_ROOT_TYPES = ['InteractionPresetOptions'] as const;

/** Anonymous upstream types that the hand-written core names; extracted for the parity test only. */
export const CORE_ALIASES: Readonly<Record<string, string>> = {
  ChartData: 'ChartAssemblyInput.data',
  ChartSpec: 'ChartAssemblyInput.chart_spec',
  ChartSize: 'ChartAssemblyInput.chart_spec.baseSize',
  ContinuousMarkCrossSection: 'AssembleOptions.continuousMarkCrossSection',
};

const LIB_MAPPED = new Set(['Partial', 'Required', 'Readonly', 'Pick', 'Omit']);

export function extractTypes(packageDir: string): Record<string, IrNamedType> {
  const manifest = packageManifest(packageDir);
  const entry = join(packageDir, manifest.types);
  const interactiveTypes = manifest.exports?.['./interactive']?.types;
  const interactive = interactiveTypes ? join(packageDir, interactiveTypes) : undefined;
  const program = ts.createProgram(interactive ? [entry, interactive] : [entry], {
    strict: true,
    noEmit: true,
    skipLibCheck: true,
    target: ts.ScriptTarget.ES2022,
    module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler,
  });
  const checker = program.getTypeChecker();
  const sourceFile = program.getSourceFile(entry);
  if (!sourceFile) throw new Error(`Cannot load ${entry}.`);
  const moduleSymbol = checker.getSymbolAtLocation(sourceFile);
  if (!moduleSymbol) throw new Error(`${entry} is not a module.`);
  const exported = new Map(checker.getExportsOfModule(moduleSymbol).map((s) => [s.name, s]));
  const interactiveSymbol = interactive ? checker.getSymbolAtLocation(program.getSourceFile(interactive)!) : undefined;
  const interactiveExports = new Map(interactiveSymbol ? checker.getExportsOfModule(interactiveSymbol).map((s) => [s.name, s]) : []);

  const resolve = (symbol: ts.Symbol): ts.Symbol =>
    symbol.flags & ts.SymbolFlags.Alias ? checker.getAliasedSymbol(symbol) : symbol;
  const isLib = (symbol: ts.Symbol) =>
    (symbol.declarations ?? []).some((d) => program.isSourceFileDefaultLibrary(d.getSourceFile()));
  const docOf = (symbol: ts.Symbol) => ts.displayPartsToString(symbol.getDocumentationComment(checker)).trim();

  const types: Record<string, IrNamedType> = {};
  const queue: ts.Symbol[] = [];
  // The IR keys named types by name; a second interface under a known name would silently take the first one's shape.
  const symbols = new Map<string, ts.Symbol>();
  const declaredIn = (symbol: ts.Symbol) => {
    const file = symbol.declarations?.[0]?.getSourceFile().fileName;
    return file ? relative(packageDir, file) : '(no declaration)';
  };
  const enqueue = (symbol: ts.Symbol) => {
    const known = symbols.get(symbol.name);
    if (known && known !== symbol) {
      throw new Error(`${symbol.name} is declared twice: ${declaredIn(known)} and ${declaredIn(symbol)}; the IR holds one type per name.`);
    }
    symbols.set(symbol.name, symbol);
    if (!(symbol.name in types) && !queue.includes(symbol)) queue.push(symbol);
  };

  const propsOf = (type: ts.Type, path: string): IrProp[] =>
    checker
      .getPropertiesOfType(type)
      .flatMap((p): IrProp[] => {
        const decl = p.declarations?.[0];
        if (!decl || !ts.isPropertySignature(decl) || !decl.type) return []; // function members are excluded
        if (ts.isFunctionTypeNode(decl.type)) return [];
        const prop: IrProp = {
          name: p.name,
          optional: (p.flags & ts.SymbolFlags.Optional) !== 0,
          type: mapNode(decl.type, `${path}.${p.name}`),
        };
        if (isNullable(decl.type)) prop.nullable = true;
        const doc = docOf(p);
        if (doc) prop.doc = doc;
        return [prop];
      })
      .sort((a, b) => ordinal(a.name, b.name));

  /** A declared type that is a union with the `null` literal, looked through parentheses only (not through aliases). */
  const isNullable = (node: ts.TypeNode): boolean => {
    while (ts.isParenthesizedTypeNode(node)) node = node.type;
    return ts.isUnionTypeNode(node) && node.types.some((n) => ts.isLiteralTypeNode(n) && n.literal.kind === ts.SyntaxKind.NullKeyword);
  };

  /** Named unions by alias symbol: the mapped union, stored under the alias name when the queue reaches the symbol. */
  const namedUnions = new Map<ts.Symbol, IrType>();
  /** Aliases whose type is being mapped, and whether it met the alias again (it refers to itself). */
  const aliasesInProgress = new Map<ts.Symbol, { recursive: boolean }>();

  /** An interface the IR records as an object (not as a record): it extends something or has no index signature. */
  const isObjectInterface = (symbol: ts.Symbol): boolean => {
    const decls = (symbol.declarations ?? []).filter(ts.isInterfaceDeclaration);
    if (decls.length === 0) return false;
    return decls.some((d) => d.heritageClauses?.length) || !decls.some((d) => d.members.some(ts.isIndexSignatureDeclaration));
  };

  /** A union of at least two members, each an inline object, a ref to an object interface or a ref to a named union. */
  const isNamedUnionShape = (t: IrType): boolean =>
    t.kind === 'union' &&
    t.members.length >= 2 &&
    t.members.every((m) => {
      if (m.kind === 'object') return true;
      if (m.kind !== 'ref') return false;
      const symbol = symbols.get(m.name);
      return symbol !== undefined && (namedUnions.has(symbol) || isObjectInterface(symbol));
    });

  const mapUnion = (nodes: readonly ts.TypeNode[], path: string): IrType => {
    const members = nodes
      .filter((n) => n.kind !== ts.SyntaxKind.UndefinedKeyword && !(ts.isLiteralTypeNode(n) && n.literal.kind === ts.SyntaxKind.NullKeyword))
      .map((n) => mapNode(n, path));
    if (members.length === 1) return members[0];
    // An alias of string literals widened by more literals (`Axes | 'available'`) is one closed set.
    const literals = members.flatMap((m) => (m.kind === 'literalUnion' && !m.open ? m.values : m.kind === 'literal' && typeof m.value === 'string' ? [m.value] : []));
    if (members.some((m) => m.kind === 'literalUnion') && members.every((m) => (m.kind === 'literalUnion' && !m.open) || (m.kind === 'literal' && typeof m.value === 'string'))) {
      return { kind: 'literalUnion', values: [...new Set(literals)] };
    }
    // `'a' | 'b' | string` takes any string; the literals are the documented values.
    if (members.some((m) => m.kind === 'string') && members.every((m) => m.kind === 'string' || (m.kind === 'literal' && typeof m.value === 'string'))) {
      return { kind: 'literalUnion', values: members.flatMap((m) => (m.kind === 'literal' ? [m.value as string] : [])), open: true };
    }
    if (members.every((m) => m.kind === 'literal' && typeof m.value === 'string')) {
      return { kind: 'literalUnion', values: members.map((m) => (m as { value: string }).value) };
    }
    if (members.length === 2 && members.every((m) => m.kind === 'literal' && typeof m.value === 'boolean')) {
      return { kind: 'boolean' };
    }
    return { kind: 'union', members };
  };

  function mapNode(node: ts.TypeNode, path: string): IrType {
    switch (node.kind) {
      case ts.SyntaxKind.StringKeyword:
        return { kind: 'string' };
      case ts.SyntaxKind.NumberKeyword:
        return { kind: 'number' };
      case ts.SyntaxKind.BooleanKeyword:
        return { kind: 'boolean' };
      case ts.SyntaxKind.AnyKeyword:
        return { kind: 'any' };
      case ts.SyntaxKind.UnknownKeyword:
        return { kind: 'unknown' };
      case ts.SyntaxKind.NeverKeyword:
        return { kind: 'never' };
    }
    if (ts.isParenthesizedTypeNode(node)) return mapNode(node.type, path);
    if (ts.isTypeOperatorNode(node) && node.operator === ts.SyntaxKind.ReadonlyKeyword) return mapNode(node.type, path);
    if (ts.isLiteralTypeNode(node)) {
      const lit = node.literal;
      if (ts.isStringLiteral(lit)) return { kind: 'literal', value: lit.text };
      if (ts.isNumericLiteral(lit)) return { kind: 'literal', value: Number(lit.text) };
      if (lit.kind === ts.SyntaxKind.TrueKeyword) return { kind: 'literal', value: true };
      if (lit.kind === ts.SyntaxKind.FalseKeyword) return { kind: 'literal', value: false };
    }
    if (ts.isUnionTypeNode(node)) return mapUnion(node.types, path);
    if (ts.isIntersectionTypeNode(node)) return { kind: 'intersection', members: node.types.map((n) => mapNode(n, path)) };
    if (ts.isArrayTypeNode(node)) return { kind: 'array', element: mapNode(node.elementType, `${path}[]`) };
    if (ts.isTupleTypeNode(node)) {
      if (node.elements.some((e) => !ts.isTypeNode(e) || ts.isNamedTupleMember(e) || ts.isRestTypeNode(e) || ts.isOptionalTypeNode(e))) {
        return { kind: 'unsupported', text: node.getText() };
      }
      return { kind: 'tuple', elements: node.elements.map((e, i) => mapNode(e, `${path}[${i}]`)) };
    }
    if (ts.isTypeLiteralNode(node)) return mapObjectType(checker.getTypeFromTypeNode(node), node.members, path);
    if (ts.isTypeReferenceNode(node)) return mapReference(node, path);
    if (ts.isIndexedAccessTypeNode(node)) {
      // `(typeof NAMES)[number]` over a `readonly [...]` tuple: the checker's literals, a closed set.
      const resolved = checker.getTypeFromTypeNode(node);
      const parts = resolved.isUnion() ? resolved.types : [resolved];
      if (parts.length > 1 && parts.every((p) => p.isStringLiteral())) {
        return { kind: 'literalUnion', values: parts.map((p) => (p as ts.StringLiteralType).value) };
      }
      // `SemanticTarget['visual']`: one object type (not an array, a tuple or a function), mapped by its members.
      if (
        !resolved.isUnionOrIntersection() &&
        resolved.flags & ts.TypeFlags.Object &&
        !checker.isArrayType(resolved) &&
        !checker.isTupleType(resolved) &&
        resolved.getCallSignatures().length === 0 &&
        checker.getIndexInfosOfType(resolved).length === 0
      ) {
        return { kind: 'object', props: propsOf(resolved, path) };
      }
    }
    return { kind: 'unsupported', text: node.getText() };
  }

  function mapObjectType(type: ts.Type, members: readonly ts.TypeElement[], path: string): IrType {
    const indexSignatures = members.filter(ts.isIndexSignatureDeclaration);
    const others = members.filter((m) => !ts.isIndexSignatureDeclaration(m));
    if (indexSignatures.length === 0) return { kind: 'object', props: propsOf(type, path) };
    const [sig] = indexSignatures;
    if (indexSignatures.length > 1 || others.length > 0 || sig.parameters[0]?.type?.kind !== ts.SyntaxKind.StringKeyword) {
      return { kind: 'unsupported', text: members.map((m) => m.getText()).join('; ') };
    }
    return { kind: 'record', value: mapNode(sig.type, `${path}{*}`) };
  }

  function mapReference(node: ts.TypeReferenceNode, path: string): IrType {
    const target = checker.getSymbolAtLocation(node.typeName);
    if (!target) return { kind: 'unsupported', text: node.getText() };
    const symbol = resolve(target);
    const args = node.typeArguments ?? [];
    if (symbol.flags & ts.SymbolFlags.TypeParameter) return { kind: 'unsupported', text: node.getText() };

    if (isLib(symbol)) {
      if ((symbol.name === 'Array' || symbol.name === 'ReadonlyArray') && args.length === 1) {
        return { kind: 'array', element: mapNode(args[0], `${path}[]`) };
      }
      if (symbol.name === 'Record' && args.length === 2) {
        // JSON keys are strings on the wire; the key kind decides the C# shape (string or int keys, or one member per literal).
        const key = mapNode(args[0], path);
        const value = mapNode(args[1], `${path}{*}`);
        if (key.kind === 'string' || (key.kind === 'literalUnion' && key.open)) return { kind: 'record', value };
        if (key.kind === 'number') return { kind: 'record', key: 'number', value };
        if (key.kind === 'literalUnion' && !key.open) return { kind: 'record', keys: key.values, value };
        if (key.kind === 'literal' && typeof key.value === 'string') return { kind: 'record', keys: [key.value], value };
        return { kind: 'unsupported', text: node.getText() };
      }
      if (symbol.name === 'Partial' && args.length === 1) {
        const inner = mapNode(args[0], path);
        if (inner.kind === 'ref') return { kind: 'partial', of: inner };
        if (inner.kind === 'record') return inner; // every entry of a map is optional already
      }
      if (symbol.name === 'Readonly' && args.length === 1) {
        // A read-only map or array has the JSON shape of the map or array.
        const inner = mapNode(args[0], path);
        if (inner.kind === 'record' || inner.kind === 'array') return inner;
      }
      if (LIB_MAPPED.has(symbol.name)) {
        return { kind: 'mapped', text: node.getText(), props: propsOf(checker.getTypeFromTypeNode(node), path) };
      }
      return { kind: 'unsupported', text: node.getText() };
    }

    const decl = symbol.declarations?.[0];
    if (!decl) return { kind: 'unsupported', text: node.getText() };
    if (ts.isTypeAliasDeclaration(decl)) {
      if (decl.typeParameters?.length) return { kind: 'unsupported', text: node.getText() };
      if (namedUnions.has(symbol)) return { kind: 'ref', name: symbol.name };
      // Met again while its own type is mapped: a ref, which holds only if the alias turns out a named union.
      const inProgress = aliasesInProgress.get(symbol);
      if (inProgress) {
        inProgress.recursive = true;
        return { kind: 'ref', name: symbol.name };
      }
      const state = { recursive: false };
      aliasesInProgress.set(symbol, state);
      const mapped = mapNode(decl.type, path);
      aliasesInProgress.delete(symbol);
      if (!isNamedUnionShape(mapped)) {
        if (state.recursive) {
          throw new Error(`${symbol.name} refers to itself (at ${path}); only a named union (a union of objects) may, because the IR inlines any other alias.`);
        }
        return mapped; // any other alias is inlined
      }
      namedUnions.set(symbol, mapped);
      enqueue(symbol);
      return { kind: 'ref', name: symbol.name };
    }
    if (ts.isInterfaceDeclaration(decl)) {
      if (decl.typeParameters?.length || args.length) return { kind: 'unsupported', text: node.getText() };
      enqueue(symbol);
      return { kind: 'ref', name: symbol.name };
    }
    return { kind: 'unsupported', text: node.getText() };
  }

  function mapInterface(symbol: ts.Symbol): IrNamedType {
    const decls = (symbol.declarations ?? []).filter(ts.isInterfaceDeclaration);
    const type = checker.getDeclaredTypeOfSymbol(symbol);
    const members = decls.flatMap((d) => [...d.members]);
    const hasHeritage = decls.some((d) => d.heritageClauses?.length);
    const named: IrNamedType = hasHeritage
      ? { kind: 'object', props: propsOf(type, symbol.name) }
      : mapObjectType(type, members, symbol.name);
    const doc = docOf(symbol);
    if (doc) named.doc = doc;
    return named;
  }

  for (const name of ROOT_TYPES) {
    const symbol = exported.get(name);
    if (!symbol) throw new Error(`flint-chart does not export ${name}.`);
    enqueue(resolve(symbol));
  }
  // Optional: a version without the interactive entry point (before 1.0.0) has no preset options.
  for (const name of interactive ? INTERACTIVE_ROOT_TYPES : []) {
    const symbol = interactiveExports.get(name);
    if (!symbol) throw new Error(`flint-chart/interactive does not export ${name}.`);
    enqueue(resolve(symbol));
  }
  while (queue.length > 0) {
    const symbol = queue.shift()!;
    if (symbol.name in types) continue;
    const union = namedUnions.get(symbol);
    if (union) {
      const doc = docOf(symbol);
      types[symbol.name] = doc ? { ...union, doc } : union;
      continue;
    }
    if (!(symbol.flags & ts.SymbolFlags.Interface)) throw new Error(`${symbol.name} is not an interface.`);
    types[symbol.name] = mapInterface(symbol);
  }

  for (const [alias, path] of Object.entries(CORE_ALIASES)) {
    if (alias in types) throw new Error(`Core alias ${alias} collides with an upstream type.`);
    types[alias] = typeAt(types, path);
  }
  return Object.fromEntries(Object.keys(types).sort(ordinal).map((k) => [k, types[k]]));
}

/** The type at a dotted IR path such as `ChartAssemblyInput.chart_spec.baseSize`. */
export function typeAt(types: Record<string, IrNamedType>, path: string): IrType {
  const [head, ...rest] = path.split('.');
  let current: IrType | undefined = types[head];
  for (const segment of rest) {
    if (current?.kind === 'ref') current = types[current.name];
    if (current?.kind !== 'object') throw new Error(`IR path ${path} does not resolve.`);
    current = current.props.find((p) => p.name === segment)?.type;
  }
  if (!current) throw new Error(`IR path ${path} does not resolve.`);
  return current;
}
