// flint-chart d.ts -> IR.types through the TypeScript checker (design §7.1). The entry point is package.json
// "types"; exports are followed by the checker, hashed declaration files are never globbed.
import { join, relative } from 'node:path';
import ts from 'typescript';
import { ordinal, type IrNamedType, type IrProp, type IrType } from '../ir.ts';
import { packageManifest } from './package.ts';

/** Exported upstream types the IR starts from: emitted (AssembleOptions, ThemeSpec) or parity-only. */
export const ROOT_TYPES = ['AssembleOptions', 'ThemeSpec', 'ChartAssemblyInput', 'ChartEncoding', 'SemanticAnnotation'] as const;

/** Anonymous upstream types that the hand-written core names; extracted for the parity test only. */
export const CORE_ALIASES: Readonly<Record<string, string>> = {
  ChartData: 'ChartAssemblyInput.data',
  ChartSpec: 'ChartAssemblyInput.chart_spec',
  ChartSize: 'ChartAssemblyInput.chart_spec.baseSize',
  ContinuousMarkCrossSection: 'AssembleOptions.continuousMarkCrossSection',
};

const LIB_MAPPED = new Set(['Partial', 'Required', 'Readonly', 'Pick', 'Omit']);

export function extractTypes(packageDir: string): Record<string, IrNamedType> {
  const entry = join(packageDir, packageManifest(packageDir).types);
  const program = ts.createProgram([entry], {
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
        const doc = docOf(p);
        if (doc) prop.doc = doc;
        return [prop];
      })
      .sort((a, b) => ordinal(a.name, b.name));

  const mapUnion = (nodes: readonly ts.TypeNode[], path: string): IrType => {
    const members = nodes
      .filter((n) => n.kind !== ts.SyntaxKind.UndefinedKeyword && !(ts.isLiteralTypeNode(n) && n.literal.kind === ts.SyntaxKind.NullKeyword))
      .map((n) => mapNode(n, path));
    if (members.length === 1) return members[0];
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
        if (args[0].kind !== ts.SyntaxKind.StringKeyword) return { kind: 'unsupported', text: node.getText() };
        return { kind: 'record', value: mapNode(args[1], `${path}{*}`) };
      }
      if (symbol.name === 'Partial' && args.length === 1) {
        const inner = mapNode(args[0], path);
        if (inner.kind === 'ref') return { kind: 'partial', of: inner };
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
      return mapNode(decl.type, path);
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
  while (queue.length > 0) {
    const symbol = queue.shift()!;
    if (symbol.name in types) continue;
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
