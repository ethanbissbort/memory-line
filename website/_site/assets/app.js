/* Memory Timeline documentation — client behaviour.
   Theme, mobile navigation, copy buttons, table-of-contents scrollspy and search.
   No dependencies; everything works from file:// as well as over HTTP. */

(function () {
  'use strict';

  var doc = document;
  var root = doc.documentElement;

  /* ------------------------------------------------------------- theme --- */

  function systemPrefersDark() {
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  function currentTheme() {
    var explicit = root.getAttribute('data-theme');
    if (explicit) return explicit;
    return systemPrefersDark() ? 'dark' : 'light';
  }

  var themeToggle = doc.querySelector('[data-theme-toggle]');
  if (themeToggle) {
    themeToggle.addEventListener('click', function () {
      var next = currentTheme() === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      try {
        localStorage.setItem('mt-theme', next);
      } catch (e) {
        /* storage unavailable — the choice just won't persist */
      }
    });
  }

  /* ------------------------------------------------------ mobile nav --- */

  var navToggle = doc.querySelector('[data-nav-toggle]');
  function setNav(open) {
    doc.body.classList.toggle('nav-open', open);
    if (navToggle) navToggle.setAttribute('aria-expanded', String(open));
  }
  if (navToggle) {
    navToggle.addEventListener('click', function () {
      setNav(!doc.body.classList.contains('nav-open'));
    });
  }
  doc.querySelectorAll('[data-nav-close]').forEach(function (el) {
    el.addEventListener('click', function () {
      setNav(false);
    });
  });
  doc.querySelectorAll('.sidebar a').forEach(function (link) {
    link.addEventListener('click', function () {
      setNav(false);
    });
  });

  /* -------------------------------------------------------- copy code --- */

  doc.querySelectorAll('[data-copy]').forEach(function (button) {
    button.addEventListener('click', function () {
      var block = button.closest('.codeblock');
      var code = block && block.querySelector('code');
      if (!code) return;

      var text = code.innerText;
      var done = function () {
        button.textContent = 'Copied';
        button.classList.add('is-copied');
        setTimeout(function () {
          button.textContent = 'Copy';
          button.classList.remove('is-copied');
        }, 1600);
      };

      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done, fallback);
      } else {
        fallback();
      }

      function fallback() {
        var area = doc.createElement('textarea');
        area.value = text;
        area.setAttribute('readonly', '');
        area.style.position = 'fixed';
        area.style.opacity = '0';
        doc.body.appendChild(area);
        area.select();
        try {
          doc.execCommand('copy');
          done();
        } catch (e) {
          button.textContent = 'Press ⌘C';
        }
        doc.body.removeChild(area);
      }
    });
  });

  /* --------------------------------------------------------- scrollspy --- */

  var tocLinks = Array.prototype.slice.call(doc.querySelectorAll('[data-toc-link]'));
  if (tocLinks.length && 'IntersectionObserver' in window) {
    var byId = {};
    tocLinks.forEach(function (link) {
      byId[decodeURIComponent(link.getAttribute('href').slice(1))] = link;
    });

    var headings = Object.keys(byId)
      .map(function (id) {
        return doc.getElementById(id);
      })
      .filter(Boolean);

    var visible = new Set();

    var markActive = function (id) {
      tocLinks.forEach(function (link) {
        link.parentElement.classList.remove('is-active');
      });
      var link = byId[id];
      if (link) link.parentElement.classList.add('is-active');
    };

    var observer = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) visible.add(entry.target.id);
          else visible.delete(entry.target.id);
        });

        var first = headings.filter(function (h) {
          return visible.has(h.id);
        })[0];

        if (first) {
          markActive(first.id);
          return;
        }
        // Nothing in the band: highlight the last heading scrolled past.
        var above = headings.filter(function (h) {
          return h.getBoundingClientRect().top < 120;
        });
        if (above.length) markActive(above[above.length - 1].id);
      },
      { rootMargin: '-80px 0px -70% 0px', threshold: 0 }
    );

    headings.forEach(function (h) {
      observer.observe(h);
    });
  }

  /* ------------------------------------------------------------ search --- */

  var modal = doc.querySelector('[data-search-modal]');
  var input = doc.querySelector('[data-search-input]');
  var results = doc.querySelector('[data-search-results]');
  var index = window.MT_SEARCH_INDEX || [];
  var selected = -1;
  var rendered = [];

  function openSearch() {
    if (!modal) return;
    modal.hidden = false;
    doc.body.style.overflow = 'hidden';
    if (input) {
      input.focus();
      input.select();
    }
  }

  function closeSearch() {
    if (!modal) return;
    modal.hidden = true;
    doc.body.style.overflow = '';
  }

  doc.querySelectorAll('[data-search-open]').forEach(function (el) {
    el.addEventListener('click', openSearch);
  });
  doc.querySelectorAll('[data-search-close]').forEach(function (el) {
    el.addEventListener('click', closeSearch);
  });

  doc.addEventListener('keydown', function (event) {
    var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName) || event.target.isContentEditable;

    if ((event.key === 'k' || event.key === 'K') && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      openSearch();
      return;
    }
    if (event.key === '/' && !typing && modal && modal.hidden) {
      event.preventDefault();
      openSearch();
      return;
    }
    if (!modal || modal.hidden) return;

    if (event.key === 'Escape') {
      event.preventDefault();
      closeSearch();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      move(1);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      move(-1);
    } else if (event.key === 'Enter' && selected >= 0 && rendered[selected]) {
      event.preventDefault();
      window.location.href = rendered[selected].href;
    }
  });

  function move(step) {
    if (!rendered.length) return;
    selected = (selected + step + rendered.length) % rendered.length;
    var nodes = results.querySelectorAll('.search-result');
    nodes.forEach(function (node, i) {
      node.classList.toggle('is-selected', i === selected);
    });
    var active = nodes[selected];
    if (active && active.scrollIntoView) active.scrollIntoView({ block: 'nearest' });
  }

  function tokenize(query) {
    return query
      .toLowerCase()
      .split(/[^a-z0-9#+._-]+/)
      .filter(function (t) {
        return t.length > 1;
      });
  }

  function score(entry, terms, phrase) {
    var heading = entry.h.toLowerCase();
    var title = entry.t.toLowerCase();
    var text = entry.x.toLowerCase();
    var total = 0;
    var matched = 0;

    for (var i = 0; i < terms.length; i += 1) {
      var term = terms[i];
      var hit = 0;

      if (heading.indexOf(term) > -1) hit += heading.indexOf(term) === 0 ? 26 : 18;
      if (title.indexOf(term) > -1) hit += 9;

      var occurrences = 0;
      var from = text.indexOf(term);
      while (from > -1 && occurrences < 12) {
        occurrences += 1;
        from = text.indexOf(term, from + term.length);
      }
      hit += Math.min(occurrences, 8) * 2;

      if (hit) matched += 1;
      total += hit;
    }

    if (!matched) return 0;
    if (matched < terms.length) total *= 0.35;
    if (phrase.length > 2) {
      if (heading.indexOf(phrase) > -1) total += 40;
      if (text.indexOf(phrase) > -1) total += 14;
    }
    return total;
  }

  function escapeHtml(text) {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  function snippet(entry, terms) {
    var text = entry.x;
    if (!text) return '';
    var lower = text.toLowerCase();
    var at = -1;
    for (var i = 0; i < terms.length && at < 0; i += 1) at = lower.indexOf(terms[i]);
    if (at < 0) at = 0;

    var start = Math.max(0, at - 90);
    var slice = text.slice(start, start + 240);
    if (start > 0) slice = '…' + slice.replace(/^\S*\s/, '');
    if (start + 240 < text.length) slice += '…';

    var html = escapeHtml(slice);
    terms.forEach(function (term) {
      var safe = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      html = html.replace(new RegExp('(' + safe + ')', 'gi'), '<mark>$1</mark>');
    });
    return html;
  }

  function render(query) {
    if (!results) return;
    rendered = [];
    selected = -1;

    var terms = tokenize(query);
    if (!terms.length) {
      results.innerHTML = '<p class="search-hint">Start typing to search every document on this site.</p>';
      return;
    }

    var phrase = query.trim().toLowerCase();
    var scored = [];
    for (var i = 0; i < index.length; i += 1) {
      var value = score(index[i], terms, phrase);
      if (value > 0) scored.push({ entry: index[i], value: value });
    }

    scored.sort(function (a, b) {
      return b.value - a.value;
    });
    scored = scored.slice(0, 40);

    if (!scored.length) {
      results.innerHTML = '<p class="search-empty">No matches for <strong>' + escapeHtml(query) + '</strong>.</p>';
      return;
    }

    var html = ['<p class="search-count">' + scored.length + (scored.length === 40 ? '+' : '') + ' results</p>'];
    scored.forEach(function (item) {
      var entry = item.entry;
      var href = entry.p + '.html' + (entry.a ? '#' + entry.a : '');
      rendered.push({ href: href });
      html.push(
        '<a class="search-result" href="' + href + '">' +
          '<span class="search-result-top">' +
          '<span class="search-result-title">' + escapeHtml(entry.h) + '</span>' +
          '<span class="search-result-page">' + escapeHtml(entry.t) + '</span>' +
          '</span>' +
          '<p class="search-result-snippet">' + snippet(entry, terms) + '</p>' +
          '</a>'
      );
    });

    results.innerHTML = html.join('');
  }

  if (input) {
    var timer = null;
    input.addEventListener('input', function () {
      clearTimeout(timer);
      var value = input.value;
      timer = setTimeout(function () {
        render(value);
      }, 80);
    });
  }
})();
