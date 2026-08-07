/**
 * A small, dependency-free syntax highlighter.
 *
 * It only needs to cover the languages that actually appear in this repository's
 * documentation (PowerShell, bash, C#, JSON, JS, XML/XAML, SQL, YAML). Rules are
 * combined into one alternation per language and applied in priority order, so a
 * match never lands inside an already-tokenised region.
 */

const R = String.raw;

const RULES = {
  bash: [
    ['comment', R`#[^\n]*`],
    ['string', R`"(?:\\[\s\S]|[^"\\])*"|'(?:[^'\\]|\\[\s\S])*'`],
    ['variable', R`\$\{[^}\n]*\}|\$[A-Za-z_]\w*`],
    ['flag', R`(?<=\s)--?[A-Za-z][\w-]*`],
    [
      'keyword',
      R`\b(?:if|then|else|elif|fi|for|while|until|do|done|case|esac|in|function|return|export|source|local|set|unset|exit|sudo)\b`,
    ],
    [
      'builtin',
      R`\b(?:npm|npx|node|yarn|pnpm|git|apt|apt-get|dnf|brew|curl|wget|dotnet|msbuild|jest|electron|chmod|mkdir|rm|cp|mv|ls|cd|echo|cat|grep|make|python3?)\b`,
    ],
    ['operator', R`\|\||&&|[|&;><]`],
    ['number', R`\b\d+(?:\.\d+)*\b`],
  ],

  powershell: [
    ['comment', R`<#[\s\S]*?#>|#[^\n]*`],
    ['string', R`@"[\s\S]*?"@|"(?:\x60[\s\S]|[^"])*"|'(?:''|[^'])*'`],
    ['variable', R`\$(?:env:)?[A-Za-z_][\w:]*`],
    ['cmdlet', R`\b[A-Z][a-zA-Z]+-[A-Z][A-Za-z0-9]+\b`],
    [
      'keyword',
      R`\b(?:if|elseif|else|foreach|ForEach-Object|for|while|switch|function|param|return|try|catch|finally|throw|begin|process|end|filter|in)\b`,
    ],
    ['flag', R`(?<=[\s(])-[A-Za-z][\w]*`],
    ['builtin', R`\b(?:dotnet|msbuild|npm|node|git|winget|choco|vstest|makeappx|signtool)\b`],
    ['operator', R`\||&&|\|\||-eq|-ne|-lt|-gt|-match|-notmatch|-and|-or`],
    ['number', R`\b\d+(?:\.\d+)*\b`],
  ],

  csharp: [
    ['comment', R`//[^\n]*|/\*[\s\S]*?\*/`],
    ['string', R`@"(?:[^"]|"")*"|\$?"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'`],
    ['attribute', R`\[[A-Z]\w*(?:\([^)\n]*\))?\]`],
    [
      'keyword',
      R`\b(?:abstract|as|async|await|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|get|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|record|ref|return|sbyte|sealed|set|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|value|var|virtual|void|volatile|when|where|while|with|yield)\b`,
    ],
    ['type', R`\b[A-Z]\w*(?:<[^<>\n]*>)?`],
    ['number', R`\b\d+(?:\.\d+)?[fdmLlUu]?\b`],
  ],

  javascript: [
    ['comment', R`//[^\n]*|/\*[\s\S]*?\*/`],
    ['string', R`\x60(?:\\[\s\S]|[^\x60\\])*\x60|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'`],
    [
      'keyword',
      R`\b(?:async|await|break|case|catch|class|const|continue|default|delete|do|else|export|extends|finally|for|from|function|if|import|in|instanceof|let|new|of|return|static|super|switch|this|throw|try|typeof|var|void|while|yield)\b`,
    ],
    ['bool', R`\b(?:true|false|null|undefined)\b`],
    ['type', R`\b[A-Z]\w*\b`],
    ['number', R`\b\d+(?:\.\d+)?\b`],
  ],

  json: [
    ['key', R`"(?:\\.|[^"\\])*"(?=\s*:)`],
    ['string', R`"(?:\\.|[^"\\])*"`],
    ['bool', R`\b(?:true|false|null)\b`],
    ['number', R`-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b`],
  ],

  xml: [
    ['comment', R`<!--[\s\S]*?-->`],
    ['tag', R`</?[A-Za-z_][\w:.-]*|/?>`],
    ['attr', R`[A-Za-z_:][\w:.-]*(?=\s*=)`],
    ['string', R`"[^"\n]*"|'[^'\n]*'`],
    ['variable', R`\{[^}\n]*\}`],
  ],

  sql: [
    ['comment', R`--[^\n]*|/\*[\s\S]*?\*/`],
    ['string', R`'(?:''|[^'])*'`],
    [
      'keyword',
      R`\b(?:SELECT|FROM|WHERE|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|INDEX|UNIQUE|ALTER|ADD|DROP|JOIN|LEFT|RIGHT|INNER|OUTER|ON|GROUP|BY|ORDER|LIMIT|OFFSET|AND|OR|NOT|NULL|PRIMARY|KEY|FOREIGN|REFERENCES|COLLATE|NOCASE|PRAGMA|BEGIN|COMMIT|ROLLBACK|AS|DISTINCT|COUNT|SUM|AVG|CASE|WHEN|THEN|ELSE|END)\b`,
    ],
    ['type', R`\b(?:INTEGER|TEXT|REAL|BLOB|NUMERIC|VARCHAR|DATETIME|BOOLEAN)\b`],
    ['number', R`\b\d+(?:\.\d+)?\b`],
  ],

  yaml: [
    ['comment', R`#[^\n]*`],
    ['key', R`(?<=^|\n)\s*-?\s*[\w.$-]+(?=\s*:)`],
    ['string', R`"(?:\\.|[^"\\])*"|'(?:''|[^'])*'`],
    ['bool', R`\b(?:true|false|null|yes|no|on|off)\b`],
    ['number', R`\b\d+(?:\.\d+)*\b`],
  ],
};

const ALIASES = {
  sh: 'bash',
  shell: 'bash',
  zsh: 'bash',
  console: 'bash',
  ps: 'powershell',
  ps1: 'powershell',
  pwsh: 'powershell',
  cs: 'csharp',
  'c#': 'csharp',
  js: 'javascript',
  jsx: 'javascript',
  mjs: 'javascript',
  ts: 'javascript',
  tsx: 'javascript',
  node: 'javascript',
  jsonc: 'json',
  html: 'xml',
  xaml: 'xml',
  csproj: 'xml',
  svg: 'xml',
  yml: 'yaml',
  sqlite: 'sql',
};

/** Languages we can actually colour, for the UI label. */
export const SUPPORTED = new Set([...Object.keys(RULES), ...Object.keys(ALIASES)]);

const compiled = new Map();

function compile(lang) {
  if (compiled.has(lang)) return compiled.get(lang);
  const rules = RULES[lang];
  const entry = rules
    ? {
        names: rules.map(([name]) => name),
        re: new RegExp(rules.map(([, src], i) => `(?<g${i}>${src})`).join('|'), 'gm'),
      }
    : null;
  compiled.set(lang, entry);
  return entry;
}

export function escapeHtml(text) {
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

export function normalizeLang(lang) {
  if (!lang) return '';
  const key = String(lang).trim().toLowerCase().split(/[\s,]/)[0];
  return ALIASES[key] || key;
}

/**
 * Highlight `code` for `lang`, returning HTML-escaped markup.
 * Unknown languages fall back to plain escaped text.
 */
export function highlight(code, lang) {
  const normalized = normalizeLang(lang);
  const entry = compile(normalized);
  if (!entry) return escapeHtml(code);

  let out = '';
  let last = 0;
  entry.re.lastIndex = 0;

  for (const match of code.matchAll(entry.re)) {
    const groups = match.groups || {};
    let name = null;
    for (let i = 0; i < entry.names.length; i += 1) {
      if (groups[`g${i}`] !== undefined) {
        name = entry.names[i];
        break;
      }
    }
    if (!name) continue;
    out += escapeHtml(code.slice(last, match.index));
    out += `<span class="tok-${name}">${escapeHtml(match[0])}</span>`;
    last = match.index + match[0].length;
  }

  out += escapeHtml(code.slice(last));
  return out;
}

/**
 * Fenced blocks in these docs are frequently ASCII diagrams or directory trees
 * rather than code. They read far better rendered as a figure with no colouring.
 */
export function looksLikeDiagram(code, lang) {
  if (lang && !['', 'text', 'txt', 'plaintext', 'plain', 'ascii', 'tree'].includes(normalizeLang(lang))) {
    return false;
  }
  return /[│├└─┌┐┘┴┬┼┤╔╗╚╝║═▼▲◄►↕↔⇄]/.test(code) || /^\s*[\w.-]+\/\s*$/m.test(code);
}
