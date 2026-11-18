/**
 * Canonical icon & legacy-Polymer estate build gate for dorc-web.
 *
 * Enforces that all UI icons in `src/` resolve through the single canonical
 * `<vaadin-iconset name="dorc" size="24">` registry in `src/icons/dorc-icons.ts`,
 * and prevents retired Polymer `<paper-*>` / `<iron-*>` elements from returning:
 *   1. No imports of retired/competing packages (`@vaadin/icons`,
 *      `@vaadin/vaadin-lumo-styles/icons`, `@polymer/*`, `line-awesome`, or
 *      `brand-icons`).
 *   2. Outside `src/icons/dorc-icons.ts`, no `<vaadin-iconset>`, `<paper-*>`,
 *      `<iron-*>`, ad-hoc inline `<svg>`, or Line Awesome classes.
 *   3. No legacy non-`dorc:` iconset namespace prefixes (`vaadin:`, `lumo:`,
 *      `brand:`, `la:`, `iron:`, `av:`, `icons:`, `hardware:`, `inline:`, etc.).
 *   4. Every `dorc:<name>` literal and `dorcIcon('<name>')` call resolves to a
 *      registered name in `DORC_ICON_NAMES`, with no untyped `dorc:...${...}`
 *      string interpolation outside `src/icons/dorc-icons.ts`.
 *   5. Every component/template rendering `<vaadin-icon>` has a runtime import
 *      of `@vaadin/icon` (type-only imports do not register custom elements),
 *      and inline `color` styles on `<vaadin-icon>` use only theme tokens
 *      (`var(--dorc-*)` / `var(--lumo-*)`) or `currentColor`/`inherit`.
 *   6. When scanning a tree that owns `icons/dorc-icons.ts`, every entry in
 *      `DORC_ICON_NAMES` has >=1 reference (no orphaned icons) and clean
 *      monochrome `currentColor` SVG definitions (no hex fills or inner IDs).
 *
 *   node tools/icon-estate-audit.mjs [--root <dir>]
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

// fileURLToPath, not `.pathname`: on Windows the latter yields `/D:/a/...`,
// which join() turns into `D:\D:\a\...`. This runs in CI on windows-latest.
const ROOT = resolve(fileURLToPath(new URL('.', import.meta.url)), '..');
const SRC = (() => {
  const flag = process.argv.indexOf('--root');
  if (flag === -1 || !process.argv[flag + 1]) {
    return join(ROOT, 'src');
  }
  const candidate = resolve(ROOT, process.argv[flag + 1]);
  const rel = relative(ROOT, candidate);
  if (rel.startsWith('..') || resolve(candidate) !== candidate) {
    throw new Error(`--root must point inside ${ROOT}`);
  }
  return candidate;
})();

const SKIP_DIRS = new Set(['apis', 'node_modules']);

const FORBIDDEN_IMPORT_PATTERNS = [
  /^@vaadin\/icons(?:\/|$)/,
  /^@vaadin\/vaadin-lumo-styles\/(?:icons|vaadin-icons|lumo-icons)(?:\.js)?$/,
  /^@polymer\//,
  /^line-awesome(?:\/|$)/,
  /(?:^|\/)brand-icons(?:\.[jt]s)?$/
];

const LEGACY_NAMESPACE_RE =
  /\b(vaadin|lumo|brand|la|iron|av|icons|hardware|social|maps|places|editor|device|notification|communication|inline):([A-Za-z0-9_-]+)\b(?!\/)/g;

const DORC_NAMESPACE_RE = /\bdorc:([A-Za-z0-9_-]*)(?!\/)/g;

const VAADIN_ICON_TAG_RE = /<\s*vaadin-icon(?:\s|>|\/|$)/i;
const VAADIN_ICON_OPEN_TAG_RE = /<\s*vaadin-icon\b[^>]*>/gi;
const VAADIN_BUTTON_BLOCK_RE =
  /(<\s*vaadin-button\b[^>]*>)([\s\S]*?)<\/\s*vaadin-button\s*>/gi;

function hasThemeToken(openTag, token) {
  const m = /\btheme\s*=\s*(?:"([^"]*)"|'([^']*)')/i.exec(openTag);
  if (!m) return false;
  const val = (m[1] ?? m[2] ?? '').trim();
  return val.split(/\s+/).includes(token);
}

const ALLOWED_ICON_COLOR_RE =
  /^(?:currentcolor|inherit|initial|unset|var\(\s*--[a-zA-Z0-9_-]+\s*(?:,\s*(?:currentcolor|inherit|initial|unset|var\(\s*--[a-zA-Z0-9_-]+\s*\))\s*)?\))$/i;

const FORBIDDEN_MARKUP_RULES = [
  {
    re: /<\s*vaadin-iconset\b/gi,
    formatMessage: () =>
      'defines <vaadin-iconset> outside src/icons/dorc-icons.ts — register all icons in the canonical dorc iconset'
  },
  {
    re: /<\s*(iron-[a-z0-9-]+)\b/gi,
    formatMessage: m =>
      `uses legacy Polymer <${m[1].toLowerCase()}> element — use Vaadin/Lit components or <vaadin-icon icon="dorc:*"> instead`
  },
  {
    re: /<\s*(paper-[a-z0-9-]+)\b/gi,
    formatMessage: m =>
      `uses legacy Polymer <${m[1].toLowerCase()}> element — use Vaadin/Lit components instead`
  },
  {
    re: /<\s*svg\b/gi,
    formatMessage: () =>
      'embeds inline <svg> outside src/icons/dorc-icons.ts — add the glyph to DORC_ICON_DEFS and render via <vaadin-icon icon="dorc:*">'
  },
  {
    re: /\bla-(?:solid|regular|brands|light|thin|duotone)\b|\bla\s+la-[a-z0-9-]+\b/g,
    formatMessage: () =>
      'uses legacy Line Awesome icon class — use <vaadin-icon icon="dorc:*"> instead'
  }
];

function inspectVaadinButtonInner(raw) {
  const iconOpenTags = [];
  let hasLabelText = false;
  const lower = raw.toLowerCase();
  let i = 0;

  while (i < raw.length) {
    if (raw.startsWith('<!--', i)) {
      const endComment = raw.indexOf('-->', i + 4);
      i = endComment === -1 ? raw.length : endComment + 3;
      continue;
    }
    if (raw[i] === '<') {
      const closeBracket = raw.indexOf('>', i + 1);
      if (closeBracket === -1) break;
      const tagText = raw.slice(i, closeBracket + 1);
      if (/^<\s*vaadin-tooltip\b/i.test(tagText)) {
        const closeTooltip = lower.indexOf('</vaadin-tooltip', closeBracket + 1);
        if (closeTooltip === -1) break;
        const closeTooltipEnd = raw.indexOf('>', closeTooltip + 16);
        i = closeTooltipEnd === -1 ? raw.length : closeTooltipEnd + 1;
        continue;
      }
      if (/^<\s*vaadin-icon\b/i.test(tagText)) {
        iconOpenTags.push({ tag: tagText, index: i });
      }
      i = closeBracket + 1;
      continue;
    }
    if (!/\s/.test(raw[i])) {
      hasLabelText = true;
    }
    i += 1;
  }

  return { iconOpenTags, hasLabelText };
}

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (!SKIP_DIRS.has(entry)) yield* walk(full);
    } else if (entry.endsWith('.ts')) {
      yield full;
    }
  }
}

function parseCanonicalIconRegistry(registryPath, text) {
  const source = ts.createSourceFile(
    registryPath,
    text,
    ts.ScriptTarget.Latest,
    true
  );
  const names = new Set();
  const duplicates = [];
  const registrySvgProblems = [];

  const ALLOWED_REGISTRY_PAINT_RE = /^(?:currentcolor|none)$/i;

  const scanRegistryInitializer = node => {
    const checkLiteral = (litNode, chunk) => {
      let reportedColor = false;

      const styleAttrRe = /\bstyle\s*=\s*(?:"([^"]*)"|'([^']*)')/gi;
      let styleAttr;
      while ((styleAttr = styleAttrRe.exec(chunk)) !== null) {
        const styleBody = styleAttr[1] ?? styleAttr[2] ?? '';
        const paintDeclRe =
          /(?:^|;)\s*(?:fill|stroke|color|stop-color|flood-color|lighting-color)\s*:\s*([^;]+)/gi;
        let paintDecl;
        while ((paintDecl = paintDeclRe.exec(styleBody)) !== null) {
          const val = paintDecl[1].replace(/\s*!important\s*$/i, '').trim();
          if (val && !ALLOWED_REGISTRY_PAINT_RE.test(val)) {
            reportedColor = true;
            registrySvgProblems.push({
              line: lineForMatch(source, litNode, styleAttr.index),
              msg: `registry SVG contains hardcoded color "${val}" — icons must inherit currentColor`
            });
          }
        }
      }

      const paintAttrRe =
        /\b(?:fill|stroke|color|stop-color|flood-color|lighting-color)\s*=\s*(?:"([^"]*)"|'([^']*)')/gi;
      let paintAttr;
      while ((paintAttr = paintAttrRe.exec(chunk)) !== null) {
        const val = (paintAttr[1] ?? paintAttr[2] ?? '').trim();
        if (val && !ALLOWED_REGISTRY_PAINT_RE.test(val)) {
          reportedColor = true;
          registrySvgProblems.push({
            line: lineForMatch(source, litNode, paintAttr.index),
            msg: `registry SVG contains hardcoded color "${val}" — icons must inherit currentColor`
          });
        }
      }

      if (!reportedColor) {
        const hex = chunk.match(/#[0-9a-fA-F]{3,8}\b/);
        if (hex) {
          registrySvgProblems.push({
            line: lineForMatch(source, litNode, hex.index ?? 0),
            msg: `registry SVG contains hardcoded color "${hex[0]}" — icons must inherit currentColor`
          });
        }
      }

      const badInner = chunk.match(/\bid\s*=|<\s*(?:style|script)\b/i);
      if (badInner) {
        registrySvgProblems.push({
          line: lineForMatch(source, litNode, badInner.index ?? 0),
          msg: `registry SVG contains forbidden "${badInner[0]}" — inner IDs/<style>/<script> collide in document.head`
        });
      }
    };
    const walkInit = child => {
      if (
        ts.isPropertyAssignment(child) &&
        child.name.getText(source) === 'viewBox' &&
        ts.isStringLiteral(child.initializer)
      ) {
        const vbMatch =
          /^(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (\d+(?:\.\d+)?) (\d+(?:\.\d+)?)$/.exec(
            child.initializer.text
          );
        if (
          !vbMatch ||
          Number(vbMatch[3]) <= 0 ||
          Number(vbMatch[4]) <= 0
        ) {
          registrySvgProblems.push({
            line: lineForMatch(source, child.initializer, 0),
            msg: `invalid viewBox "${child.initializer.text}" in DORC_ICON_DEFS — expected "<minX> <minY> <width> <height>" with positive width and height`
          });
        }
        return;
      }
      if (
        ts.isStringLiteral(child) ||
        ts.isNoSubstitutionTemplateLiteral(child) ||
        ts.isTemplateHead(child) ||
        ts.isTemplateMiddle(child) ||
        ts.isTemplateTail(child)
      ) {
        checkLiteral(child, child.text);
      }
      ts.forEachChild(child, walkInit);
    };
    walkInit(node);
  };

  const visit = node => {
    if (
      ts.isVariableDeclaration(node) &&
      ts.isIdentifier(node.name) &&
      node.initializer
    ) {
      if (node.name.text === 'DORC_ICON_NAMES') {
        const init = ts.isAsExpression(node.initializer)
          ? node.initializer.expression
          : node.initializer;
        if (ts.isArrayLiteralExpression(init)) {
          for (const elem of init.elements) {
            if (ts.isStringLiteral(elem)) {
              if (names.has(elem.text)) duplicates.push(elem.text);
              names.add(elem.text);
            }
          }
        }
      } else if (node.name.text === 'DORC_ICON_DEFS') {
        scanRegistryInitializer(node.initializer);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(source);

  if (names.size === 0) {
    throw new Error(
      `Failed to parse non-empty DORC_ICON_NAMES from ${registryPath}`
    );
  }

  return {
    names,
    duplicates,
    registrySvgProblems
  };
}

function lineForMatch(source, node, matchIndex) {
  // Approximate offset inside the literal token (+1 skips opening quote/`}`/`\``)
  const pos = Math.min(
    node.getEnd(),
    node.getStart(source) + 1 + Math.max(0, matchIndex)
  );
  return source.getLineAndCharacterOfPosition(pos).line + 1;
}

const fileEntries = [];
for (const file of walk(SRC)) {
  fileEntries.push({
    file,
    relFromSrc: relative(SRC, file).replace(/\\/g, '/'),
    text: readFileSync(file, 'utf8')
  });
}

const localRegistryEntry = fileEntries.find(
  entry => entry.relFromSrc === 'icons/dorc-icons.ts'
);
const hasLocalRegistry = Boolean(localRegistryEntry);
const defaultRegistryPath = join(ROOT, 'src', 'icons', 'dorc-icons.ts');
const registryPath = localRegistryEntry
  ? localRegistryEntry.file
  : defaultRegistryPath;
const registryText = localRegistryEntry
  ? localRegistryEntry.text
  : readFileSync(defaultRegistryPath, 'utf8');

const {
  names: canonicalNames,
  duplicates,
  registrySvgProblems
} = parseCanonicalIconRegistry(registryPath, registryText);

let offenders = 0;
let sites = 0;
const filesWithIcons = new Set();
const usedCanonicalNames = new Set();

const report = (file, line, msg) => {
  offenders += 1;
  const rel = relative(ROOT, file).replace(/\\/g, '/');
  console.log(`${rel}:${line}  ${msg}`);
};

for (const dup of duplicates) {
  report(
    registryPath,
    1,
    `duplicate canonical icon ID "${dup}" in DORC_ICON_NAMES`
  );
}

if (hasLocalRegistry) {
  for (const prob of registrySvgProblems) {
    report(registryPath, prob.line, prob.msg);
  }
}

function isRuntimeImport(node) {
  if (!node.importClause) return true; // side-effect `import '@vaadin/icon'`
  if (node.importClause.isTypeOnly) return false;
  if (node.importClause.name) return true;
  const bindings = node.importClause.namedBindings;
  if (!bindings) return false;
  if (ts.isNamespaceImport(bindings)) return true;
  return (
    bindings.elements.length > 0 && bindings.elements.some(el => !el.isTypeOnly)
  );
}

function isRuntimeExport(node) {
  if (node.isTypeOnly) return false;
  if (!node.exportClause) return true;
  if (ts.isNamespaceExport(node.exportClause)) return true;
  return (
    node.exportClause.elements.length > 0 &&
    node.exportClause.elements.some(el => !el.isTypeOnly)
  );
}

function isAllowedIconColorValue(raw) {
  const cleaned = raw
    .trim()
    .replace(/\s*!important\s*$/i, '')
    .trim();
  return cleaned.length === 0 || ALLOWED_ICON_COLOR_RE.test(cleaned);
}

function unwrapExpr(expr) {
  let cur = expr;
  while (
    cur &&
    (ts.isParenthesizedExpression(cur) ||
      ts.isAsExpression(cur) ||
      ts.isSatisfiesExpression(cur) ||
      ts.isNonNullExpression(cur))
  ) {
    cur = cur.expression;
  }
  return cur;
}

function collectLeafStringLiterals(expr, out = []) {
  const unwrapped = unwrapExpr(expr);
  if (!unwrapped) return out;
  if (
    ts.isStringLiteral(unwrapped) ||
    ts.isNoSubstitutionTemplateLiteral(unwrapped)
  ) {
    out.push({ node: unwrapped, text: unwrapped.text });
  } else if (ts.isConditionalExpression(unwrapped)) {
    collectLeafStringLiterals(unwrapped.whenTrue, out);
    collectLeafStringLiterals(unwrapped.whenFalse, out);
  } else if (ts.isBinaryExpression(unwrapped)) {
    collectLeafStringLiterals(unwrapped.left, out);
    collectLeafStringLiterals(unwrapped.right, out);
  }
  return out;
}

const filesImportingVaadinIcon = new Set();
const filesRenderingVaadinIcon = [];

for (const { file, relFromSrc, text } of fileEntries) {
  const isRegistryFile = relFromSrc === 'icons/dorc-icons.ts';
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);

  const checkImportSpecifier = (specNode, specText, isRuntime) => {
    if (isRuntime && /^@vaadin\/icon(?:\/|$)/.test(specText)) {
      filesImportingVaadinIcon.add(file);
    }
    if (FORBIDDEN_IMPORT_PATTERNS.some(re => re.test(specText))) {
      const line =
        source.getLineAndCharacterOfPosition(specNode.getStart(source)).line +
        1;
      report(
        file,
        line,
        `forbidden legacy package import "${specText}" — use Vaadin/Lit components and import src/icons/dorc-icons.js`
      );
    }
  };

  let firstVaadinIconLine = 0;

  const checkColorValueExpression = (expr, fallbackLine) => {
    if (!expr) return;
    for (const lit of collectLeafStringLiterals(expr)) {
      if (!isAllowedIconColorValue(lit.text)) {
        report(
          file,
          lineForMatch(source, lit.node, 0) || fallbackLine,
          `sets hardcoded inline color "${lit.text.trim()}" on <vaadin-icon> — use var(--dorc-*) / var(--lumo-*) tokens or currentColor so dark theme stays legible`
        );
      }
    }
  };

  const scanStyleDeclarationList = (styleBody, exprList, styleLine) => {
    for (const rawDecl of styleBody.split(';')) {
      const decl = rawDecl.trim();
      if (!decl) continue;

      const colorProp = /^color\s*:\s*([\s\S]+)$/i.exec(decl);
      if (colorProp) {
        const rawVal = colorProp[1].trim();
        const pureExpr = /^\uE000(\d+)\uE001$/.exec(rawVal);
        if (pureExpr) {
          const [, idxText] = pureExpr;
          checkColorValueExpression(exprList.at(Number(idxText)), styleLine);
          continue;
        }
        if (!isAllowedIconColorValue(rawVal)) {
          const displayVal = rawVal
            .replace(/\uE000\d+\uE001/g, '${...}')
            .replace(/\s*!important\s*$/i, '')
            .trim();
          report(
            file,
            styleLine,
            `sets hardcoded inline color "${displayVal}" on <vaadin-icon> — use var(--dorc-*) / var(--lumo-*) tokens or currentColor so dark theme stays legible`
          );
        }
        continue;
      }

      if (!/^[A-Za-z-]+\s*:/.test(decl)) {
        const placeholderRe = /\uE000(\d+)\uE001/g;
        let ph;
        while ((ph = placeholderRe.exec(decl)) !== null) {
          const [, idxText] = ph;
          checkWholeStyleExpression(exprList.at(Number(idxText)), styleLine);
        }
      }
    }
  };

  const checkWholeStyleExpression = (expr, fallbackLine) => {
    const unwrapped = unwrapExpr(expr);
    if (!unwrapped) return;

    if (
      ts.isStringLiteral(unwrapped) ||
      ts.isNoSubstitutionTemplateLiteral(unwrapped)
    ) {
      scanStyleDeclarationList(
        unwrapped.text,
        [],
        lineForMatch(source, unwrapped, 0) || fallbackLine
      );
      return;
    }

    if (ts.isTemplateExpression(unwrapped)) {
      let combined = unwrapped.head.text;
      const nestedExprs = [];
      for (const span of unwrapped.templateSpans) {
        const exprIdx = nestedExprs.length;
        nestedExprs.push(span.expression);
        combined += `\uE000${exprIdx}\uE001${span.literal.text}`;
      }
      scanStyleDeclarationList(
        combined,
        nestedExprs,
        lineForMatch(source, unwrapped, 0) || fallbackLine
      );
      return;
    }

    if (ts.isConditionalExpression(unwrapped)) {
      checkWholeStyleExpression(unwrapped.whenTrue, fallbackLine);
      checkWholeStyleExpression(unwrapped.whenFalse, fallbackLine);
      return;
    }

    if (ts.isBinaryExpression(unwrapped)) {
      checkWholeStyleExpression(unwrapped.left, fallbackLine);
      checkWholeStyleExpression(unwrapped.right, fallbackLine);
      return;
    }

    if (ts.isCallExpression(unwrapped)) {
      const callee = ts.isIdentifier(unwrapped.expression)
        ? unwrapped.expression.text
        : '';
      const [styleObj] = unwrapped.arguments;
      if (
        callee === 'styleMap' &&
        styleObj &&
        ts.isObjectLiteralExpression(styleObj)
      ) {
        for (const prop of styleObj.properties) {
          if (
            ts.isPropertyAssignment(prop) &&
            prop.name.getText(source).replace(/['"]/g, '') === 'color'
          ) {
            checkColorValueExpression(prop.initializer, fallbackLine);
          }
        }
      }
    }
  };

  const scanMarkupSurface = (surfaceText, lineAtOffset, expressions = []) => {
    if (isRegistryFile) return;

    for (const rule of FORBIDDEN_MARKUP_RULES) {
      rule.re.lastIndex = 0;
      let m;
      while ((m = rule.re.exec(surfaceText)) !== null) {
        report(file, lineAtOffset(m.index), rule.formatMessage(m));
      }
    }

    if (firstVaadinIconLine === 0) {
      const tagMatch = VAADIN_ICON_TAG_RE.exec(surfaceText);
      if (tagMatch) {
        firstVaadinIconLine = lineAtOffset(tagMatch.index);
      }
    }

    VAADIN_ICON_OPEN_TAG_RE.lastIndex = 0;
    let openTagMatch;
    while (
      (openTagMatch = VAADIN_ICON_OPEN_TAG_RE.exec(surfaceText)) !== null
    ) {
      const openTag = openTagMatch[0];
      const tagStart = openTagMatch.index;
      const styleAttrRe =
        /\bstyle\s*=\s*(?:"([^"]*)"|'([^']*)'|(\uE000\d+\uE001))/gi;
      let styleAttr;
      while ((styleAttr = styleAttrRe.exec(openTag)) !== null) {
        const styleLine = lineAtOffset(tagStart + styleAttr.index);
        if (styleAttr[3]) {
          const exprIdx = Number(styleAttr[3].slice(1, -1));
          checkWholeStyleExpression(expressions.at(exprIdx), styleLine);
          continue;
        }
        const styleBody = styleAttr[1] ?? styleAttr[2] ?? '';
        scanStyleDeclarationList(styleBody, expressions, styleLine);
      }
    }

    VAADIN_BUTTON_BLOCK_RE.lastIndex = 0;
    let btnMatch;
    while ((btnMatch = VAADIN_BUTTON_BLOCK_RE.exec(surfaceText)) !== null) {
      const [, btnOpenTag, btnInnerRaw] = btnMatch;
      const btnStart = btnMatch.index;
      const { iconOpenTags, hasLabelText } =
        inspectVaadinButtonInner(btnInnerRaw);
      if (iconOpenTags.length === 0) continue;

      if (hasLabelText) {
        if (hasThemeToken(btnOpenTag, 'icon')) {
          report(
            file,
            lineAtOffset(btnStart),
            'labeled <vaadin-button> sets theme="icon" — omit theme="icon" when button has visible label text'
          );
        }
        for (const { tag: iconTag, index: iconOffset } of iconOpenTags) {
          const iconLine = lineAtOffset(
            btnStart + btnOpenTag.length + iconOffset
          );
          if (
            !/\bslot\s*=\s*(?:"(?:prefix|suffix)"|'(?:prefix|suffix)')/i.test(
              iconTag
            )
          ) {
            report(
              file,
              iconLine,
              '<vaadin-icon> inside labeled <vaadin-button> is missing slot="prefix" or slot="suffix" — unslotted icons fall into block-level [part="label"] and misalign vertically'
            );
          }
          if (hasThemeToken(iconTag, 'icon')) {
            report(
              file,
              iconLine,
              '<vaadin-icon> sets unsupported theme="icon" — use slot="prefix" or slot="suffix" on the icon instead'
            );
          }
        }
      }
    }
  };

  const scanNamespaceChunk = (node, chunk) => {
    LEGACY_NAMESPACE_RE.lastIndex = 0;
    let legacyMatch;
    while ((legacyMatch = LEGACY_NAMESPACE_RE.exec(chunk)) !== null) {
      report(
        file,
        lineForMatch(source, node, legacyMatch.index),
        `uses non-canonical icon namespace "${legacyMatch[0]}" — register or use a semantic "dorc:*" icon ID instead`
      );
    }

    const trailingDynamicMatch =
      ts.isTemplateHead(node) || ts.isTemplateMiddle(node)
        ? /\bdorc:([A-Za-z0-9_-]*)$/.exec(chunk)
        : null;

    if (trailingDynamicMatch && !isRegistryFile) {
      report(
        file,
        lineForMatch(source, node, trailingDynamicMatch.index),
        `constructs dynamic "dorc:${trailingDynamicMatch[1]}\${...}" icon ID — use dorcIcon(name) or a literal "dorc:<name>" so every icon reference is statically verified`
      );
    }

    DORC_NAMESPACE_RE.lastIndex = 0;
    let dorcMatch;
    while ((dorcMatch = DORC_NAMESPACE_RE.exec(chunk)) !== null) {
      if (
        trailingDynamicMatch !== null &&
        dorcMatch.index === trailingDynamicMatch.index
      ) {
        continue;
      }

      const slug = dorcMatch[1];
      if (!slug || !/^[a-z0-9-]+$/.test(slug) || !canonicalNames.has(slug)) {
        report(
          file,
          lineForMatch(source, node, dorcMatch.index),
          `references unknown dorc icon "dorc:${slug}" — add "${slug}" to DORC_ICON_NAMES in src/icons/dorc-icons.ts or fix the typo`
        );
      } else {
        sites += 1;
        filesWithIcons.add(file);
        usedCanonicalNames.add(slug);
      }
    }
  };

  const visit = node => {
    if (
      ts.isImportDeclaration(node) &&
      ts.isStringLiteral(node.moduleSpecifier)
    ) {
      checkImportSpecifier(
        node.moduleSpecifier,
        node.moduleSpecifier.text,
        isRuntimeImport(node)
      );
      return;
    }

    if (
      ts.isExportDeclaration(node) &&
      node.moduleSpecifier &&
      ts.isStringLiteral(node.moduleSpecifier)
    ) {
      checkImportSpecifier(
        node.moduleSpecifier,
        node.moduleSpecifier.text,
        isRuntimeExport(node)
      );
      return;
    }

    if (ts.isCallExpression(node)) {
      const [firstArg] = node.arguments;
      if (
        node.expression.kind === ts.SyntaxKind.ImportKeyword &&
        firstArg &&
        ts.isStringLiteral(firstArg)
      ) {
        checkImportSpecifier(firstArg, firstArg.text, true);
      }

      const calleeName = ts.isIdentifier(node.expression)
        ? node.expression.text
        : ts.isPropertyAccessExpression(node.expression)
          ? node.expression.name.text
          : '';
      if (calleeName === 'dorcIcon' && firstArg) {
        if (
          ts.isStringLiteral(firstArg) ||
          ts.isNoSubstitutionTemplateLiteral(firstArg)
        ) {
          if (!canonicalNames.has(firstArg.text)) {
            report(
              file,
              lineForMatch(source, firstArg, 0),
              `references unknown dorc icon "dorc:${firstArg.text}" via dorcIcon() — add "${firstArg.text}" to DORC_ICON_NAMES in src/icons/dorc-icons.ts or fix the typo`
            );
          } else {
            sites += 1;
            filesWithIcons.add(file);
            usedCanonicalNames.add(firstArg.text);
          }
          // Skip normal string-literal visit on firstArg (which holds bare slug without `dorc:`)
          return;
        }
      }
    }

    if (ts.isTemplateExpression(node)) {
      let combined = node.head.text;
      const segments = [
        { outStart: 0, outEnd: combined.length, litNode: node.head }
      ];
      const expressions = [];
      for (const span of node.templateSpans) {
        const exprIdx = expressions.length;
        expressions.push(span.expression);
        combined += `\uE000${exprIdx}\uE001`;
        const segStart = combined.length;
        combined += span.literal.text;
        segments.push({
          outStart: segStart,
          outEnd: combined.length,
          litNode: span.literal
        });
      }
      const lineAtOffset = offset => {
        const seg =
          segments.find(s => offset <= s.outEnd) ??
          segments[segments.length - 1];
        return lineForMatch(
          source,
          seg.litNode,
          Math.max(0, offset - seg.outStart)
        );
      };
      scanMarkupSurface(combined, lineAtOffset, expressions);
    } else if (
      ts.isStringLiteral(node) ||
      ts.isNoSubstitutionTemplateLiteral(node)
    ) {
      scanMarkupSurface(node.text, offset =>
        lineForMatch(source, node, offset)
      );
    }

    if (
      ts.isStringLiteral(node) ||
      ts.isNoSubstitutionTemplateLiteral(node) ||
      ts.isTemplateHead(node) ||
      ts.isTemplateMiddle(node) ||
      ts.isTemplateTail(node)
    ) {
      scanNamespaceChunk(node, node.text);
    }

    ts.forEachChild(node, visit);
  };

  visit(source);

  if (firstVaadinIconLine > 0) {
    filesRenderingVaadinIcon.push({ file, line: firstVaadinIconLine });
  }
}

for (const { file, line } of filesRenderingVaadinIcon) {
  const companionHost = file.endsWith('.template.ts')
    ? file.replace(/\.template\.ts$/, '.ts')
    : null;
  const hasImport =
    filesImportingVaadinIcon.has(file) ||
    (companionHost !== null && filesImportingVaadinIcon.has(companionHost));
  if (!hasImport) {
    report(
      file,
      line,
      'renders <vaadin-icon> without importing "@vaadin/icon" — add import \'@vaadin/icon\' so the custom element is registered in isolated routes and unit tests'
    );
  }
}

if (hasLocalRegistry) {
  for (const name of canonicalNames) {
    if (!usedCanonicalNames.has(name)) {
      report(
        registryPath,
        1,
        `unused canonical icon "dorc:${name}" in DORC_ICON_NAMES — remove dead icons or wire a call-site`
      );
    }
  }
}

if (offenders > 0) {
  console.log(
    `\n${offenders} icon estate violation(s) found (${sites} valid dorc:* reference(s) scanned).`
  );
  process.exitCode = 1;
} else {
  console.log(
    `${sites} dorc:* icon reference(s) across ${filesWithIcons.size} file(s) verified against ${canonicalNames.size} canonical IDs, 0 violation(s).`
  );
}
