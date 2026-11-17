/**
 * Canonical icon estate build gate for dorc-web.
 *
 * Enforces that all UI icons in `src/` resolve through the single canonical
 * `<vaadin-iconset name="dorc" size="24">` registry in `src/icons/dorc-icons.ts`:
 *   1. No imports of retired/competing icon packages (`@vaadin/icons`,
 *      `@vaadin/vaadin-lumo-styles/icons`, `@polymer/iron-icon*`,
 *      `@polymer/paper-icon-button`, `line-awesome`, or `brand-icons`).
 *   2. Outside `src/icons/dorc-icons.ts`, no `<vaadin-iconset>`, `<iron-icon>`,
 *      `<paper-icon-button>`, ad-hoc inline `<svg>`, or Line Awesome classes.
 *   3. No legacy non-`dorc:` iconset namespace prefixes (`vaadin:`, `lumo:`,
 *      `brand:`, `la:`, `iron:`, `av:`, `icons:`, `hardware:`, `inline:`, etc.).
 *   4. Every `dorc:<name>` literal and `dorcIcon('<name>')` call resolves to a
 *      registered name in `DORC_ICON_NAMES`, with no untyped `dorc:${...}`
 *      string interpolation outside `src/icons/dorc-icons.ts`.
 *   5. Every component/template rendering `<vaadin-icon>` imports `@vaadin/icon`
 *      (directly or via its companion `.ts` host), and never sets hardcoded
 *      hex/named CSS colors on `<vaadin-icon>` instead of `--dorc-*`/`--lumo-*`.
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
  /^@polymer\/(?:iron-icon|iron-icons|paper-icon-button)(?:\/|$)/,
  /^line-awesome(?:\/|$)/,
  /(?:^|\/)brand-icons(?:\.[jt]s)?$/
];

const LEGACY_NAMESPACE_RE =
  /\b(vaadin|lumo|brand|la|iron|av|icons|hardware|social|maps|places|editor|device|notification|communication|inline):([A-Za-z0-9_-]+)\b(?!\/)/g;

const DORC_NAMESPACE_RE = /\bdorc:([A-Za-z0-9_-]*)(?!\/)/g;

const VAADIN_ICON_TAG_RE = /<\s*vaadin-icon(?:\s|>|\/|$)/i;

const HARDCODED_ICON_COLOR_RE =
  /<\s*vaadin-icon\b[^>]*\bstyle\s*=\s*["'][^"']*\bcolor\s*:\s*(#[0-9a-fA-F]{3,8}\b|\b(?:red|darkred|green|darkgreen|blue|cornflowerblue|orange|grey|gray|black|white|crimson|goldenrod|purple|teal)\b)/gi;

const FORBIDDEN_MARKUP_RULES = [
  {
    re: /<\s*vaadin-iconset\b/gi,
    message:
      'defines <vaadin-iconset> outside src/icons/dorc-icons.ts — register all icons in the canonical dorc iconset'
  },
  {
    re: /<\s*iron-icon\b/gi,
    message: 'uses legacy <iron-icon> — use <vaadin-icon icon="dorc:*"> instead'
  },
  {
    re: /<\s*paper-icon-button\b/gi,
    message:
      'uses legacy <paper-icon-button> — use <vaadin-button theme="icon"> with <vaadin-icon icon="dorc:*"> instead'
  },
  {
    re: /<\s*svg\b/gi,
    message:
      'embeds inline <svg> outside src/icons/dorc-icons.ts — add the glyph to DORC_ICON_DEFS and render via <vaadin-icon icon="dorc:*">'
  },
  {
    re: /\bla-(?:solid|regular|brands|light|thin|duotone)\b|\bla\s+la-[a-z0-9-]+\b/g,
    message:
      'uses legacy Line Awesome icon class — use <vaadin-icon icon="dorc:*"> instead'
  }
];

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

  const scanRegistryInitializer = node => {
    const checkLiteral = (litNode, chunk) => {
      const hex = chunk.match(/#[0-9a-fA-F]{3,8}\b/);
      if (hex) {
        registrySvgProblems.push({
          line: lineForMatch(source, litNode, hex.index ?? 0),
          msg: `registry SVG contains hardcoded color "${hex[0]}" — icons must inherit currentColor`
        });
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
        if (!/^0 0 \d+(?:\.\d+)? \d+(?:\.\d+)?$/.test(child.initializer.text)) {
          registrySvgProblems.push({
            line: lineForMatch(source, child.initializer, 0),
            msg: `invalid viewBox "${child.initializer.text}" in DORC_ICON_DEFS — expected "0 0 <width> <height>"`
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

const filesImportingVaadinIcon = new Set();
const filesRenderingVaadinIcon = [];

for (const { file, relFromSrc, text } of fileEntries) {
  const isRegistryFile = relFromSrc === 'icons/dorc-icons.ts';
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);

  const checkImportSpecifier = (specNode, specText) => {
    if (/^@vaadin\/icon(?:\/|$)/.test(specText)) {
      filesImportingVaadinIcon.add(file);
    }
    if (FORBIDDEN_IMPORT_PATTERNS.some(re => re.test(specText))) {
      const line =
        source.getLineAndCharacterOfPosition(specNode.getStart(source)).line +
        1;
      report(
        file,
        line,
        `forbidden legacy icon import "${specText}" — import src/icons/dorc-icons.js and use <vaadin-icon icon="dorc:*">`
      );
    }
  };

  let firstVaadinIconLine = 0;

  const scanTextChunk = (node, chunk) => {
    if (!isRegistryFile) {
      for (const rule of FORBIDDEN_MARKUP_RULES) {
        rule.re.lastIndex = 0;
        let m;
        while ((m = rule.re.exec(chunk)) !== null) {
          report(file, lineForMatch(source, node, m.index), rule.message);
        }
      }

      if (firstVaadinIconLine === 0) {
        const tagMatch = VAADIN_ICON_TAG_RE.exec(chunk);
        if (tagMatch) {
          firstVaadinIconLine = lineForMatch(source, node, tagMatch.index);
        }
      }

      HARDCODED_ICON_COLOR_RE.lastIndex = 0;
      let colorMatch;
      while ((colorMatch = HARDCODED_ICON_COLOR_RE.exec(chunk)) !== null) {
        report(
          file,
          lineForMatch(source, node, colorMatch.index),
          `sets hardcoded inline color "${colorMatch[1]}" on <vaadin-icon> — use var(--dorc-*) / var(--lumo-*) tokens or currentColor so dark theme stays legible`
        );
      }
    }

    LEGACY_NAMESPACE_RE.lastIndex = 0;
    let legacyMatch;
    while ((legacyMatch = LEGACY_NAMESPACE_RE.exec(chunk)) !== null) {
      report(
        file,
        lineForMatch(source, node, legacyMatch.index),
        `uses non-canonical icon namespace "${legacyMatch[0]}" — register or use a semantic "dorc:*" icon ID instead`
      );
    }

    const hasTrailingDynamicDorcPrefix =
      (ts.isTemplateHead(node) || ts.isTemplateMiddle(node)) &&
      /\bdorc:$/.test(chunk);

    if (hasTrailingDynamicDorcPrefix && !isRegistryFile) {
      const prefixIdx = chunk.lastIndexOf('dorc:');
      report(
        file,
        lineForMatch(source, node, prefixIdx),
        'constructs dynamic "dorc:${...}" icon ID — use dorcIcon(name) or a literal "dorc:<name>" so every icon reference is statically verified'
      );
    }

    DORC_NAMESPACE_RE.lastIndex = 0;
    let dorcMatch;
    while ((dorcMatch = DORC_NAMESPACE_RE.exec(chunk)) !== null) {
      const slug = dorcMatch[1];
      const isAtTemplateInterpolationBoundary =
        hasTrailingDynamicDorcPrefix &&
        slug === '' &&
        dorcMatch.index + dorcMatch[0].length === chunk.length;
      if (isAtTemplateInterpolationBoundary) continue;

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
      checkImportSpecifier(node.moduleSpecifier, node.moduleSpecifier.text);
      return;
    }

    if (
      ts.isExportDeclaration(node) &&
      node.moduleSpecifier &&
      ts.isStringLiteral(node.moduleSpecifier)
    ) {
      checkImportSpecifier(node.moduleSpecifier, node.moduleSpecifier.text);
      return;
    }

    if (ts.isCallExpression(node)) {
      const [firstArg] = node.arguments;
      if (
        node.expression.kind === ts.SyntaxKind.ImportKeyword &&
        firstArg &&
        ts.isStringLiteral(firstArg)
      ) {
        checkImportSpecifier(firstArg, firstArg.text);
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

    if (
      ts.isStringLiteral(node) ||
      ts.isNoSubstitutionTemplateLiteral(node) ||
      ts.isTemplateHead(node) ||
      ts.isTemplateMiddle(node) ||
      ts.isTemplateTail(node)
    ) {
      scanTextChunk(node, node.text);
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
