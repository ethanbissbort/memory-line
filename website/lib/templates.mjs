/**
 * HTML templates: the page shell, the generated landing page, and the
 * documentation map.
 */

import { Marked } from 'marked';
import { escapeHtml } from './highlight.mjs';

const inlineMarked = new Marked({ gfm: true });

/** Render a short markdown string (bold / code / links) without a block wrapper. */
export function inline(text) {
  return inlineMarked.parseInline(String(text ?? ''));
}

export function block(text) {
  return inlineMarked.parse(String(text ?? ''));
}

const FAVICON =
  'data:image/svg+xml,' +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect width="32" height="32" rx="7" fill="#1d1b18"/><path d="M4 21h24" stroke="#8b8175" stroke-width="1.5" stroke-linecap="round"/><circle cx="9" cy="21" r="3.1" fill="#e0873f"/><circle cx="17" cy="14" r="3.6" fill="#f0b46e"/><circle cx="25" cy="18" r="2.6" fill="#6ec2b8"/><path d="M9 21 17 14 25 18" stroke="#8b8175" stroke-width="1.2" fill="none" stroke-linejoin="round" opacity=".8"/></svg>`
  );

const THEME_BOOTSTRAP = `(function(){try{var t=localStorage.getItem('mt-theme');if(t==='light'||t==='dark'){document.documentElement.setAttribute('data-theme',t);}}catch(e){}})();`;

/**
 * The shared page shell.
 */
export function layout({
  title,
  description,
  bodyClass = '',
  sidebar,
  content,
  siteName,
  repoUrl,
  activeSlug,
  buildTime,
  pageCount,
}) {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>${escapeHtml(title)}</title>
<meta name="description" content="${escapeHtml(description || '')}">
<meta property="og:title" content="${escapeHtml(title)}">
<meta property="og:description" content="${escapeHtml(description || '')}">
<meta property="og:type" content="website">
<link rel="icon" href="${FAVICON}">
<link rel="stylesheet" href="assets/styles.css">
<script>${THEME_BOOTSTRAP}</script>
</head>
<body class="${bodyClass}" data-page="${escapeHtml(activeSlug)}">
<a class="skip-link" href="#main">Skip to content</a>

<header class="topbar">
  <div class="topbar-inner">
    <button class="icon-btn nav-toggle" type="button" aria-label="Toggle navigation" aria-expanded="false" data-nav-toggle>
      <svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true"><path d="M3 5h14M3 10h14M3 15h14" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" fill="none"/></svg>
    </button>
    <a class="brand" href="index.html">
      <span class="brand-mark" aria-hidden="true">
        <svg viewBox="0 0 32 32" width="26" height="26"><path d="M3 22h26" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" opacity=".45"/><circle cx="8" cy="22" r="3" fill="currentColor" opacity=".55"/><circle cx="16" cy="14" r="3.8" fill="currentColor"/><circle cx="25" cy="18.5" r="2.6" fill="currentColor" opacity=".7"/></svg>
      </span>
      <span class="brand-text">
        <span class="brand-name">${escapeHtml(siteName)}</span>
        <span class="brand-sub">Documentation</span>
      </span>
    </a>

    <button class="searchbar" type="button" data-search-open aria-label="Search documentation">
      <svg viewBox="0 0 20 20" width="15" height="15" aria-hidden="true"><circle cx="9" cy="9" r="5.5" stroke="currentColor" stroke-width="1.7" fill="none"/><path d="M13.2 13.2 17 17" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/></svg>
      <span>Search the docs</span>
      <kbd>/</kbd>
    </button>

    <div class="topbar-actions">
      <button class="icon-btn" type="button" data-theme-toggle aria-label="Switch colour theme" title="Switch colour theme">
        <svg class="icon-sun" viewBox="0 0 20 20" width="17" height="17" aria-hidden="true"><circle cx="10" cy="10" r="4" fill="currentColor"/><path d="M10 1.5v2M10 16.5v2M1.5 10h2M16.5 10h2M4 4l1.4 1.4M14.6 14.6 16 16M16 4l-1.4 1.4M5.4 14.6 4 16" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>
        <svg class="icon-moon" viewBox="0 0 20 20" width="17" height="17" aria-hidden="true"><path d="M16.5 12.4A7 7 0 0 1 7.6 3.5a7 7 0 1 0 8.9 8.9Z" fill="currentColor"/></svg>
      </button>
      <a class="icon-btn" href="${escapeHtml(repoUrl)}" target="_blank" rel="noopener noreferrer" aria-label="View the repository on GitHub" title="Repository on GitHub">
        <svg viewBox="0 0 16 16" width="17" height="17" aria-hidden="true"><path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8a8 8 0 0 0 5.47 7.59c.4.07.55-.17.55-.38l-.01-1.33c-2.23.48-2.7-1.07-2.7-1.07-.36-.93-.89-1.18-.89-1.18-.73-.5.05-.49.05-.49.81.06 1.23.83 1.23.83.72 1.23 1.88.87 2.34.67.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.6 7.6 0 0 1 4 0c1.53-1.03 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.28.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48l-.01 2.2c0 .21.15.46.55.38A8 8 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/></svg>
      </a>
    </div>
  </div>
</header>

<div class="shell">
  <aside class="sidebar" data-sidebar>
    <nav aria-label="Documentation">
${sidebar}
    </nav>
  </aside>
  <div class="sidebar-scrim" data-nav-close hidden></div>

  <main class="main" id="main">
${content}
    <footer class="site-footer">
      <div>
        <strong>${escapeHtml(siteName)}</strong> documentation ·
        ${pageCount} documents ·
        generated ${escapeHtml(buildTime)}
      </div>
      <div class="footer-links">
        <a href="index.html">Home</a>
        <a href="documentation-map.html">All documentation</a>
        <a href="${escapeHtml(repoUrl)}" target="_blank" rel="noopener noreferrer">GitHub</a>
      </div>
      <div class="footer-note">
        This site is generated from the markdown in the repository — edit the source files and rebuild.
      </div>
    </footer>
  </main>
</div>

<div class="search-modal" data-search-modal hidden>
  <div class="search-backdrop" data-search-close></div>
  <div class="search-panel" role="dialog" aria-modal="true" aria-label="Search documentation">
    <div class="search-input-row">
      <svg viewBox="0 0 20 20" width="17" height="17" aria-hidden="true"><circle cx="9" cy="9" r="5.5" stroke="currentColor" stroke-width="1.7" fill="none"/><path d="M13.2 13.2 17 17" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/></svg>
      <input type="search" placeholder="Search headings and content…" data-search-input autocomplete="off" spellcheck="false" aria-label="Search query">
      <button class="icon-btn" type="button" data-search-close aria-label="Close search"><kbd>Esc</kbd></button>
    </div>
    <div class="search-results" data-search-results>
      <p class="search-hint">Start typing to search every document on this site.</p>
    </div>
  </div>
</div>

<script src="assets/search-index.js" defer></script>
<script src="assets/app.js" defer></script>
</body>
</html>
`;
}

/** Sidebar navigation markup. */
export function renderSidebar(sections, activeSlug, tocForActive) {
  const parts = ['      <ul class="nav">'];
  for (const section of sections) {
    parts.push(`        <li class="nav-section">`);
    parts.push(`          <p class="nav-section-title">${escapeHtml(section.title)}</p>`);
    parts.push(`          <ul class="nav-list">`);
    for (const page of section.pages) {
      const isActive = page.slug === activeSlug;
      const cls = ['nav-link'];
      if (isActive) cls.push('is-active');
      parts.push(
        `            <li><a class="${cls.join(' ')}" href="${page.slug}.html"${
          isActive ? ' aria-current="page"' : ''
        }>${escapeHtml(page.shortTitle || page.title)}</a>`
      );
      if (isActive && tocForActive && tocForActive.length) {
        parts.push('              <ul class="nav-sub">');
        for (const item of tocForActive.filter((t) => t.depth === 2).slice(0, 30)) {
          parts.push(
            `                <li><a class="nav-sublink" href="#${item.id}">${escapeHtml(item.text)}</a></li>`
          );
        }
        parts.push('              </ul>');
      }
      parts.push('            </li>');
    }
    parts.push('          </ul>');
    parts.push('        </li>');
  }
  parts.push('      </ul>');
  return parts.join('\n');
}

/** The right-hand "on this page" rail. */
export function renderToc(toc) {
  if (!toc || toc.length < 2) return '';
  const items = toc
    .map(
      (t) =>
        `<li class="toc-item toc-h${t.depth}"><a href="#${t.id}" data-toc-link>${escapeHtml(t.text)}</a></li>`
    )
    .join('\n');
  return `<aside class="toc" aria-label="On this page">
  <p class="toc-title">On this page</p>
  <ul class="toc-list">
${items}
  </ul>
  <a class="toc-top" href="#main">↑ Back to top</a>
</aside>`;
}

/** Header block above a rendered document. */
export function renderDocHeader(page, meta, repoUrl, branch) {
  const tags = (page.tags || [])
    .map((t) => `<span class="tag">${escapeHtml(t)}</span>`)
    .join('');
  return `<header class="doc-header">
  <nav class="breadcrumb" aria-label="Breadcrumb">
    <a href="index.html">Docs</a>
    <span aria-hidden="true">/</span>
    <span>${escapeHtml(page.sectionTitle)}</span>
  </nav>
  <h1>${escapeHtml(page.title)}</h1>
  ${page.description ? `<p class="doc-lede">${inline(page.description)}</p>` : ''}
  <div class="doc-meta">
    ${page.audience ? `<span class="doc-meta-item"><b>For</b> ${escapeHtml(page.audience)}</span>` : ''}
    <span class="doc-meta-item"><b>${meta.words.toLocaleString('en-US')}</b> words</span>
    <span class="doc-meta-item"><b>${meta.minutes}</b> min read</span>
    <a class="doc-meta-item doc-source" href="${escapeHtml(repoUrl)}/blob/${escapeHtml(branch)}/${escapeHtml(page.source)}" target="_blank" rel="noopener noreferrer">
      ${escapeHtml(page.source)} ↗
    </a>
  </div>
  ${tags ? `<div class="tags">${tags}</div>` : ''}
</header>`;
}

/** Previous / next footer navigation. */
export function renderPager(prev, next) {
  if (!prev && !next) return '';
  const cell = (page, kind) =>
    page
      ? `<a class="pager-link pager-${kind}" href="${page.slug}.html">
      <span class="pager-kind">${kind === 'prev' ? '← Previous' : 'Next →'}</span>
      <span class="pager-title">${escapeHtml(page.shortTitle || page.title)}</span>
    </a>`
      : '<span class="pager-link is-empty" aria-hidden="true"></span>';
  return `<nav class="pager" aria-label="Document navigation">
  ${cell(prev, 'prev')}
  ${cell(next, 'next')}
</nav>`;
}

/* ------------------------------------------------------------------ */
/* Landing page                                                        */
/* ------------------------------------------------------------------ */

const HERO_SVG = `<svg class="hero-svg" viewBox="0 0 520 220" role="img" aria-label="Stylised timeline with event bubbles, an era band and an uncertainty range">
  <defs>
    <linearGradient id="eraA" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="currentColor" stop-opacity=".16"/>
      <stop offset="100%" stop-color="currentColor" stop-opacity=".02"/>
    </linearGradient>
  </defs>
  <rect x="28" y="26" width="150" height="150" rx="10" fill="url(#eraA)"/>
  <rect x="214" y="26" width="118" height="150" rx="10" fill="url(#eraA)"/>
  <rect x="368" y="26" width="124" height="150" rx="10" fill="url(#eraA)"/>
  <text x="34" y="44" class="hero-era">Childhood</text>
  <text x="220" y="44" class="hero-era">University</text>
  <text x="374" y="44" class="hero-era">Berlin</text>

  <line x1="20" y1="140" x2="500" y2="140" class="hero-axis"/>
  ${[60, 130, 200, 270, 340, 410, 470]
    .map((x, i) => `<line x1="${x}" y1="134" x2="${x}" y2="146" class="hero-tick"/>`)
    .join('')}
  ${['1994', '1999', '2003', '2008', '2013', '2018', '2024']
    .map((label, i) => `<text x="${[60, 130, 200, 270, 340, 410, 470][i]}" y="162" class="hero-year">${label}</text>`)
    .join('')}

  <rect x="176" y="104" width="52" height="12" rx="6" class="hero-uncertain"/>
  <rect x="292" y="72" width="86" height="10" rx="5" class="hero-span"/>

  <circle cx="62" cy="118" r="9" class="hero-dot dot-a"/>
  <circle cx="128" cy="94" r="7" class="hero-dot dot-b"/>
  <circle cx="202" cy="110" r="11" class="hero-dot dot-a"/>
  <circle cx="268" cy="77" r="8" class="hero-dot dot-c"/>
  <circle cx="336" cy="112" r="9" class="hero-dot dot-b"/>
  <circle cx="404" cy="88" r="12" class="hero-dot dot-c"/>
  <circle cx="468" cy="120" r="7" class="hero-dot dot-a"/>

  <path d="M62 118 128 94 202 110 268 77 336 112 404 88 468 120" class="hero-thread"/>
</svg>`;

export function renderLanding({ landing, site, sections, pageIndex, totals }) {
  const pill = (p) =>
    `<span class="pill${p.tone === 'primary' ? ' pill-primary' : ''}">${escapeHtml(p.label)}</span>`;

  const statCard = (s) => `<div class="stat">
      <div class="stat-value">${escapeHtml(s.value)}</div>
      <div class="stat-label">${escapeHtml(s.label)}</div>
      <div class="stat-hint">${escapeHtml(s.hint)}</div>
    </div>`;

  const pipelineStep = (s, i) => `<li class="pipe-step">
      <div class="pipe-index">${i + 1}</div>
      <div class="pipe-icon" aria-hidden="true">${s.icon}</div>
      <div class="pipe-body">
        <h3>${escapeHtml(s.title)}</h3>
        <p>${inline(s.detail)}</p>
        <p class="pipe-sub">${inline(s.sub)}</p>
      </div>
    </li>`;

  const featureCard = (g) => `<article class="feature-card">
      <div class="feature-icon" aria-hidden="true">${g.icon}</div>
      <h3>${escapeHtml(g.title)}</h3>
      <ul>${g.items.map((i) => `<li>${inline(i)}</li>`).join('')}</ul>
    </article>`;

  const layerRow = (l, i) => `<div class="layer" style="--layer-index:${i}">
      <div class="layer-name"><code>${escapeHtml(l.name)}</code></div>
      <div class="layer-role">${escapeHtml(l.role)}</div>
      <div class="layer-detail">${inline(l.detail)}</div>
    </div>`;

  const decision = (d) => `<article class="decision">
      <h3>${inline(d.title)}</h3>
      <p>${inline(d.body)}</p>
    </article>`;

  const pathCard = (p) => `<article class="path-card">
      <div class="path-icon" aria-hidden="true">${p.icon}</div>
      <h3>${escapeHtml(p.title)}</h3>
      <p>${escapeHtml(p.body)}</p>
      <ul class="path-links">
        ${p.links
          .map((slug) => {
            const page = pageIndex.get(slug);
            if (!page) return '';
            return `<li><a href="${slug}.html">${escapeHtml(page.shortTitle || page.title)}</a></li>`;
          })
          .join('')}
      </ul>
    </article>`;

  const milestone = (m) => `<li class="milestone is-${escapeHtml(m.state)}">
      <div class="milestone-when">${escapeHtml(m.when)}</div>
      <div class="milestone-body">
        <h3>${escapeHtml(m.title)}${m.state === 'active' ? ' <span class="chip chip-active">in progress</span>' : ''}</h3>
        <p>${inline(m.body)}</p>
        ${m.link ? `<a class="milestone-link" href="${m.link}.html">Read the detail →</a>` : ''}
      </div>
    </li>`;

  return `
<div class="landing">
  <section class="hero">
    <div class="hero-copy">
      <p class="eyebrow">Project documentation</p>
      <h1>${escapeHtml(site.name)}</h1>
      <p class="hero-tagline">${escapeHtml(site.tagline)}</p>
      <div class="pills">${landing.status.map(pill).join('')}</div>
      <div class="hero-actions">
        <a class="btn btn-primary" href="overview.html">Read the overview</a>
        <a class="btn" href="windows-native.html">Windows Native app</a>
        <a class="btn btn-quiet" href="documentation-map.html">Browse all ${totals.docs} documents</a>
      </div>
    </div>
    <div class="hero-visual">${HERO_SVG}</div>
  </section>

  <section class="stats-strip">
    ${landing.stats.map(statCard).join('')}
  </section>

  <section class="section" id="what-it-is">
    <div class="section-head">
      <h2>What it is</h2>
    </div>
    <div class="lede">
      ${landing.intro.map((p) => `<p>${inline(p)}</p>`).join('')}
    </div>
  </section>

  <section class="section" id="pipeline">
    <div class="section-head">
      <h2>${escapeHtml(landing.pipeline.title)}</h2>
      <p class="section-lead">${inline(landing.pipeline.lead)}</p>
    </div>
    <ol class="pipeline">
      ${landing.pipeline.steps.map(pipelineStep).join('')}
    </ol>
    <div class="callout callout-note"><div class="callout-body"><p>${inline(landing.pipeline.note)}</p></div></div>
  </section>

  <section class="section" id="features">
    <div class="section-head">
      <h2>What's inside</h2>
      <p class="section-lead">The feature surface of the Windows Native app, grouped by what you are trying to do. The full list lives in the <a href="overview.html#features-windows-native">project overview</a>.</p>
    </div>
    <div class="feature-grid">
      ${landing.featureGroups.map(featureCard).join('')}
    </div>
  </section>

  <section class="section" id="architecture">
    <div class="section-head">
      <h2>${escapeHtml(landing.architecture.title)}</h2>
      <p class="section-lead">${inline(landing.architecture.lead)}</p>
    </div>
    <div class="layer-stack">
      ${landing.architecture.layers.map(layerRow).join('')}
    </div>
    <h3 class="subhead">Decisions worth knowing</h3>
    <div class="decision-grid">
      ${landing.architecture.decisions.map(decision).join('')}
    </div>
  </section>

  <section class="section" id="stack">
    <div class="section-head">
      <h2>Tech stack</h2>
    </div>
    <div class="table-wrap">
      <table class="stack-table">
        <thead><tr><th>Layer</th><th>Technology</th></tr></thead>
        <tbody>
          ${landing.stack.map(([layer, tech]) => `<tr><td>${escapeHtml(layer)}</td><td>${inline(tech)}</td></tr>`).join('')}
        </tbody>
      </table>
    </div>
  </section>

  <section class="section" id="find-your-way">
    <div class="section-head">
      <h2>Find your way</h2>
      <p class="section-lead">${landing.paths.length} routes through ${totals.docs} documents and roughly ${totals.words.toLocaleString('en-US')} words on the Windows Native app.</p>
    </div>
    <div class="path-grid">
      ${landing.paths.map(pathCard).join('')}
    </div>
  </section>

  <section class="section" id="history">
    <div class="section-head">
      <h2>How it got here</h2>
    </div>
    <ol class="milestones">
      ${landing.timeline.map(milestone).join('')}
    </ol>
  </section>

  <section class="section split" id="roadmap">
    <div>
      <div class="section-head"><h2>Roadmap</h2></div>
      <ul class="checklist">
        ${landing.roadmap.map((r) => `<li>${inline(r)}</li>`).join('')}
      </ul>
    </div>
    <div>
      <div class="section-head"><h2>Privacy &amp; security</h2></div>
      <dl class="deflist">
        ${landing.privacy.map(([term, def]) => `<dt>${escapeHtml(term)}</dt><dd>${inline(def)}</dd>`).join('')}
      </dl>
    </div>
  </section>

  <section class="section" id="all-docs">
    <div class="section-head">
      <h2>Every document</h2>
      <p class="section-lead">Grouped exactly as they appear in the sidebar.</p>
    </div>
    ${sections
      .map(
        (s) => `<div class="doc-section">
      <h3>${escapeHtml(s.title)}</h3>
      <p class="doc-section-blurb">${escapeHtml(s.blurb)}</p>
      <div class="doc-card-grid">
        ${s.pages
          .filter((p) => !p.hideFromCards)
          .map(
            (p) => `<a class="doc-card" href="${p.slug}.html">
            <h4>${escapeHtml(p.title)}</h4>
            <p>${escapeHtml(p.description)}</p>
            <span class="doc-card-meta">${p.stats ? `${p.stats.minutes} min read` : 'Read'} →</span>
          </a>`
          )
          .join('')}
      </div>
    </div>`
      )
      .join('')}
  </section>
</div>`;
}

/* ------------------------------------------------------------------ */
/* Documentation map                                                   */
/* ------------------------------------------------------------------ */

export function renderDocMap({ sections, totals, repoUrl, branch }) {
  const row = (page) => `<article class="map-entry">
    <div class="map-entry-head">
      <h3><a href="${page.slug}.html">${escapeHtml(page.title)}</a></h3>
      <a class="map-source" href="${escapeHtml(repoUrl)}/blob/${escapeHtml(branch)}/${escapeHtml(page.source)}" target="_blank" rel="noopener noreferrer"><code>${escapeHtml(page.source)}</code> ↗</a>
    </div>
    <p class="map-desc">${inline(page.description)}</p>
    <div class="map-meta">
      ${page.audience ? `<span class="tag">${escapeHtml(page.audience)}</span>` : ''}
      <span class="map-stat">${page.stats.words.toLocaleString('en-US')} words</span>
      <span class="map-stat">${page.stats.minutes} min read</span>
      <span class="map-stat">${page.stats.headings} sections</span>
    </div>
    ${
      page.toc && page.toc.length
        ? `<details class="map-toc">
      <summary>${page.toc.filter((t) => t.depth === 2).length} top-level sections</summary>
      <ul>${page.toc
        .filter((t) => t.depth === 2)
        .map((t) => `<li><a href="${page.slug}.html#${t.id}">${escapeHtml(t.text)}</a></li>`)
        .join('')}</ul>
    </details>`
        : ''
    }
  </article>`;

  return `
<header class="doc-header">
  <nav class="breadcrumb" aria-label="Breadcrumb">
    <a href="index.html">Docs</a><span aria-hidden="true">/</span><span>Start here</span>
  </nav>
  <h1>All documentation</h1>
  <p class="doc-lede">Every document covering the Windows Native app, rendered and searchable. ${totals.docs} documents, ${totals.words.toLocaleString('en-US')} words, about ${totals.minutes} minutes of reading in total.</p>
</header>

<div class="prose map-body">
${sections
  .map(
    (s) => `<section class="map-section">
  <h2 id="${escapeHtml(s.id)}" class="anchored">${escapeHtml(s.title)}<a class="heading-anchor" href="#${escapeHtml(s.id)}" aria-label="Link to this section">#</a></h2>
  <p class="section-lead">${escapeHtml(s.blurb)}</p>
  ${s.pages
    .filter((p) => p.source)
    .map(row)
    .join('')}
</section>`
  )
  .join('')}
</div>`;
}
