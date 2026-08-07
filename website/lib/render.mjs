/**
 * Markdown → site HTML.
 *
 * Beyond plain rendering this adds the things that make long technical docs
 * browsable: stable GitHub-compatible heading anchors, an extracted table of
 * contents, admonition callouts, status chips in tables, syntax highlighting,
 * ASCII-diagram figures, copyable code blocks, and repo-aware link rewriting.
 */

import { Marked } from 'marked';
import path from 'node:path';
import { escapeHtml, highlight, looksLikeDiagram, normalizeLang } from './highlight.mjs';

/** GitHub-compatible heading slugs, so in-document anchors keep working. */
export function slugify(text, seen) {
  const base =
    String(text)
      .trim()
      .toLowerCase()
      .replace(/[^\p{L}\p{N}\s_-]/gu, '')
      .replace(/\s/g, '-')
      .replace(/-{4,}/g, '---') || 'section';
  if (!seen) return base;
  const count = seen.get(base) || 0;
  seen.set(base, count + 1);
  return count === 0 ? base : `${base}-${count}`;
}

const ADMONITIONS = [
  { kind: 'danger', test: /^(?:⚠️?\s*)?(?:danger|critical|breaking)\b/i },
  { kind: 'warning', test: /^(?:⚠️?\s*)?(?:warning|caution|important|heads[- ]up)\b/i },
  { kind: 'note', test: /^(?:ℹ️?\s*)?(?:note|notes?\s+on|security note|reality check)\b/i },
  { kind: 'tip', test: /^(?:💡\s*)?(?:tip|hint|recommended)\b/i },
];

const STATUS_CHIPS = [
  { re: /^✅/, tone: 'ok' },
  { re: /^🔄/, tone: 'active' },
  { re: /^🚧/, tone: 'active' },
  { re: /^⚠️?/, tone: 'warn' },
  { re: /^❌/, tone: 'off' },
  { re: /^⏳/, tone: 'pending' },
  { re: /^◻️?/, tone: 'pending' },
];

const LANG_LABELS = {
  bash: 'Shell',
  powershell: 'PowerShell',
  csharp: 'C#',
  javascript: 'JavaScript',
  json: 'JSON',
  xml: 'XML',
  sql: 'SQL',
  yaml: 'YAML',
  diff: 'Diff',
  ini: 'INI',
  dockerfile: 'Dockerfile',
};

/**
 * @param {object} options
 * @param {string} options.sourceDir  repo-relative directory of the document
 * @param {Map<string,string>} options.pathToSlug  repo path → page slug
 * @param {string} options.repoUrl
 * @param {string} options.branch
 */
/**
 * Runs of `**Label:** value` lines are metadata blocks, not wrapped prose.
 * Without hard breaks they collapse onto a single line, so add them explicitly —
 * the rest of the corpus hard-wraps real prose, which must keep flowing.
 */
function keepMetadataLineBreaks(markdown) {
  const lines = markdown.split('\n');
  const isMeta = (line) => /^\*\*[^*\n]+:\*\*\s*\S/.test(line);
  return lines
    .map((line, i) => {
      if (!isMeta(line) || /\s{2}$/.test(line)) return line;
      if (isMeta(lines[i + 1] || '')) return `${line}  `;
      if (isMeta(lines[i - 1] || '')) return `${line}  `;
      return line;
    })
    .join('\n');
}

export function renderMarkdown(markdown, options) {
  const { sourceDir, pathToSlug, repoUrl, branch } = options;
  const seen = new Map();
  const toc = [];

  const marked = new Marked({ gfm: true, breaks: false });

  marked.use({
    renderer: {
      heading({ tokens, depth }) {
        const text = this.parser.parseInline(tokens);
        const plain = stripTags(text);
        const id = slugify(plain, seen);
        if (depth >= 2 && depth <= 3) {
          toc.push({ id, depth, text: plain });
        }
        return (
          `<h${depth} id="${id}" class="anchored">` +
          `${text}` +
          `<a class="heading-anchor" href="#${id}" aria-label="Link to this section">#</a>` +
          `</h${depth}>\n`
        );
      },

      code({ text, lang }) {
        const language = normalizeLang(lang);

        if (looksLikeDiagram(text, lang)) {
          return (
            `<figure class="diagram">` +
            `<pre aria-label="Diagram">${escapeHtml(text)}</pre>` +
            `</figure>\n`
          );
        }

        const label = LANG_LABELS[language] || (lang ? String(lang).trim() : '');
        const body = highlight(text, lang);
        return (
          `<div class="codeblock" data-lang="${escapeHtml(language || 'text')}">` +
          `<div class="codeblock-bar">` +
          `<span class="codeblock-lang">${escapeHtml(label || 'text')}</span>` +
          `<button class="codeblock-copy" type="button" data-copy aria-label="Copy code">Copy</button>` +
          `</div>` +
          `<pre><code class="language-${escapeHtml(language || 'text')}">${body}</code></pre>` +
          `</div>\n`
        );
      },

      blockquote({ tokens }) {
        const inner = this.parser.parse(tokens);
        const lead = stripTags(inner).trim().replace(/\s+/g, ' ').slice(0, 60);
        const hit = ADMONITIONS.find((a) => a.test.test(lead));
        if (hit) {
          return `<div class="callout callout-${hit.kind}"><div class="callout-body">${inner}</div></div>\n`;
        }
        if (/^<h[1-6]/.test(inner.trim())) {
          return `<div class="callout callout-feature"><div class="callout-body">${inner}</div></div>\n`;
        }
        return `<blockquote>${inner}</blockquote>\n`;
      },

      table(token) {
        let header = '';
        for (const cell of token.header) header += this.tablecell(cell);
        let body = '';
        for (const row of token.rows) {
          let cells = '';
          for (const cell of row) cells += this.tablecell(cell);
          body += this.tablerow({ text: cells });
        }
        return (
          `<div class="table-wrap"><table>` +
          `<thead>${this.tablerow({ text: header })}</thead>` +
          (body ? `<tbody>${body}</tbody>` : '') +
          `</table></div>\n`
        );
      },

      tablecell(token) {
        const tag = token.header ? 'th' : 'td';
        let content = this.parser.parseInline(token.tokens);
        if (!token.header) content = chipify(content);
        const align = token.align ? ` align="${token.align}"` : '';
        return `<${tag}${align}>${content}</${tag}>\n`;
      },

      link({ href, title, tokens }) {
        const text = this.parser.parseInline(tokens);
        const resolved = resolveHref(href, { sourceDir, pathToSlug, repoUrl, branch });
        const attrs = [`href="${escapeHtml(resolved.href)}"`];
        if (title) attrs.push(`title="${escapeHtml(title)}"`);
        if (resolved.external) attrs.push('target="_blank"', 'rel="noopener noreferrer"');
        if (resolved.className) attrs.push(`class="${resolved.className}"`);
        return `<a ${attrs.join(' ')}>${text}</a>`;
      },

      checkbox({ checked }) {
        return `<span class="task-box${checked ? ' is-checked' : ''}" aria-hidden="true"></span>`;
      },

      listitem(token) {
        const body = this.parser.parse(token.tokens, !!token.loose);
        if (token.task) {
          return `<li class="task-item${token.checked ? ' is-done' : ''}">${body}</li>\n`;
        }
        return `<li>${body}</li>\n`;
      },
    },
  });

  const html = marked.parse(keepMetadataLineBreaks(markdown));
  return { html, toc };
}

/** Turn a leading status emoji in a table cell into a coloured chip. */
function chipify(cellHtml) {
  const plain = cellHtml.trim();
  if (!plain || plain.includes('<')) return cellHtml;
  const hit = STATUS_CHIPS.find((c) => c.re.test(plain));
  if (!hit) return cellHtml;
  return `<span class="chip chip-${hit.tone}">${plain}</span>`;
}

function stripTags(html) {
  return String(html)
    .replace(/<[^>]*>/g, '')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'");
}

const DIRECTORY_HINTS = new Set([
  'src',
  'docs',
  'tests',
  'scripts',
  'public',
  'windows-native',
  'packaging',
]);

/**
 * Rewrite a markdown link so it points somewhere useful on the generated site:
 * documented `.md` files become site pages, other repo paths become GitHub links,
 * and external links are left alone (but opened in a new tab).
 */
export function resolveHref(href, { sourceDir, pathToSlug, repoUrl, branch }) {
  const raw = String(href || '').trim();

  if (!raw) return { href: '#', external: false };
  if (/^(?:[a-z][a-z0-9+.-]*:)/i.test(raw) && !/^file:/i.test(raw)) {
    const external = /^https?:/i.test(raw);
    return { href: raw, external, className: external ? 'external-link' : '' };
  }
  if (raw.startsWith('#')) return { href: raw, external: false };

  const hashIndex = raw.indexOf('#');
  const hash = hashIndex >= 0 ? raw.slice(hashIndex) : '';
  let target = hashIndex >= 0 ? raw.slice(0, hashIndex) : raw;

  if (!target) return { href: hash, external: false };

  const fromRoot = target.startsWith('/');
  const joined = fromRoot ? target.slice(1) : path.posix.join(sourceDir || '.', target);
  const normalized = path.posix.normalize(joined).replace(/^\.\//, '').replace(/\/+$/, '');

  const slug = pathToSlug.get(normalized) || pathToSlug.get(normalized.replace(/^\.\.\//, ''));
  if (slug) return { href: `${slug}.html${hash}`, external: false };

  const isDir = !path.posix.extname(normalized) || DIRECTORY_HINTS.has(normalized);
  const kind = isDir ? 'tree' : 'blob';
  return {
    href: `${repoUrl}/${kind}/${branch}/${normalized}${hash}`,
    external: true,
    className: 'external-link repo-link',
  };
}

/** Plain text of a document, for search indexing and excerpts. */
export function toPlainText(html) {
  return stripTags(
    String(html)
      .replace(/<figure class="diagram">[\s\S]*?<\/figure>/g, ' ')
      .replace(/<script[\s\S]*?<\/script>/g, ' ')
      .replace(/<\/(p|li|h[1-6]|tr|div|pre)>/g, ' $& ')
  )
    .replace(/\s+/g, ' ')
    .trim();
}
