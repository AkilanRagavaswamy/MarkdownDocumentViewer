/*
 * Markdown Viewer - Simple — reading surface script (no frameworks).
 *
 * Host -> JS (ExecuteScriptAsync):
 *   mdv.setAppearance(objOrJson)   mdv.scrollToAnchor(id)   mdv.find(query, backwards)   mdv.clearFind()
 *   mdv.collapseAll()   mdv.expandAll()   mdv.getScrollY()   mdv.setScrollY(y)
 * JS -> Host (chrome.webview.postMessage):
 *   {type:'ready'} {type:'heading', id} {type:'find', current, total} {type:'link', href} {type:'scroll', y} {type:'key', key}
 *
 * setAppearance accepts (all fields optional):
 *   {
 *     theme: 'light'|'dark'          (or isDark: bool)
 *     wrap: bool                     (or wrapCodeLines)
 *     columns: 'auto'|1|2|3          (also 'Auto'|'One'|'Two'|'Three', or columnMode)
 *     maxColumns: int, twoColumnMinWidth: px, threeColumnMinWidth: px
 *     vars: { '--mdv-bg': '#FFFFFF', ... }   (any top-level '--mdv-*' key is applied too)
 *     loadRemote: bool              (or loadRemoteContent)
 *     density: 'comfortable'|'compact'|'ultra'   (informational; exposed as data-density)
 *   }
 * Numeric values for length variables (font size, gaps, heading sizes) get a 'px' suffix.
 */
(function () {
  'use strict';

  var doc = document;
  var root = doc.documentElement;

  // ------------------------------------------------------------------ state

  var state = {
    content: null,
    columnsMode: 'auto',
    maxColumns: 3,
    twoMin: 1400,
    threeMin: 1900,
    columns: 1,
    paged: false,
    hits: [],
    hitIndex: -1,
    lastQuery: '',
    headings: [],
    visibleHeadings: new Set(),
    currentHeading: null,
    ready: false
  };

  var LENGTH_VARS = {
    '--mdv-font-size': true, '--mdv-gap': true, '--mdv-heading-gap': true,
    '--mdv-h1': true, '--mdv-h2': true, '--mdv-h3': true, '--mdv-code-size': true
  };

  var reducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ---------------------------------------------------------------- helpers

  function post(msg) {
    try {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(msg);
      }
    } catch (e) {
      /* host gone (closing tab) */
    }
  }

  function debounce(fn, ms) {
    var t = 0;
    return function () {
      var args = arguments;
      clearTimeout(t);
      t = setTimeout(function () { fn.apply(null, args); }, ms);
    };
  }

  function throttle(fn, ms) {
    var last = 0;
    var pending = 0;
    return function () {
      var now = Date.now();
      var wait = ms - (now - last);
      if (wait <= 0) {
        clearTimeout(pending);
        pending = 0;
        last = now;
        fn();
      } else if (!pending) {
        pending = setTimeout(function () {
          pending = 0;
          last = Date.now();
          fn();
        }, wait);
      }
    };
  }

  function scrollBehavior() {
    return reducedMotion ? 'auto' : 'smooth';
  }

  function isEditable(el) {
    if (!el || el === doc.body) {
      return false;
    }
    var tag = el.tagName;
    return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
  }

  function px(v) {
    var n = parseFloat(v);
    return isNaN(n) ? 0 : n;
  }

  function scroller() {
    return state.paged ? state.content : (doc.scrollingElement || root);
  }

  // ------------------------------------------------------------- structure

  function ensureContent() {
    var el = doc.getElementById('mdv-content');
    if (!el) {
      el = doc.createElement('main');
      el.id = 'mdv-content';
      var nodes = Array.prototype.slice.call(doc.body.childNodes);
      for (var i = 0; i < nodes.length; i++) {
        var n = nodes[i];
        if (n.nodeType === 1 && (n.tagName === 'SCRIPT' || n.tagName === 'STYLE')) {
          continue;
        }
        el.appendChild(n);
      }
      doc.body.insertBefore(el, doc.body.firstChild);
    }
    state.content = el;
  }

  function wrapTables() {
    var tables = state.content.querySelectorAll('table');
    for (var i = 0; i < tables.length; i++) {
      var t = tables[i];
      if (t.parentElement && t.parentElement.classList.contains('mdv-table-wrap')) {
        continue;
      }
      var wrap = doc.createElement('div');
      wrap.className = 'mdv-table-wrap';
      wrap.tabIndex = 0; // scrollable region must be keyboard reachable
      wrap.setAttribute('role', 'region');
      wrap.setAttribute('aria-label', 'Table');
      t.parentNode.insertBefore(wrap, t);
      wrap.appendChild(t);
    }
  }

  function makeChevron() {
    var ns = 'http://www.w3.org/2000/svg';
    var svg = doc.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 12 12');
    svg.setAttribute('aria-hidden', 'true');
    var path = doc.createElementNS(ns, 'path');
    path.setAttribute('d', 'M2.5 4.5L6 8l3.5-3.5');
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', 'currentColor');
    path.setAttribute('stroke-width', '1.6');
    path.setAttribute('stroke-linecap', 'round');
    path.setAttribute('stroke-linejoin', 'round');
    svg.appendChild(path);
    return svg;
  }

  function headingText(h) {
    var clone = h.cloneNode(true);
    var btn = clone.querySelector('.mdv-fold');
    if (btn) {
      btn.remove();
    }
    return (clone.textContent || '').trim();
  }

  function updateFoldButton(section) {
    var btn = section.querySelector(':scope > h2 > .mdv-fold');
    if (!btn) {
      return;
    }
    var folded = section.classList.contains('mdv-folded');
    var label = (folded ? 'Expand section ' : 'Collapse section ') + headingText(section.firstElementChild);
    btn.setAttribute('aria-expanded', folded ? 'false' : 'true');
    btn.setAttribute('aria-label', label);
    btn.title = folded ? 'Expand section' : 'Collapse section';
  }

  function setFolded(section, folded) {
    section.classList.toggle('mdv-folded', folded);
    updateFoldButton(section);
  }

  // Wrap each top-level H2 and the siblings that follow it (until the next H1/H2) in a section.
  function buildSections() {
    var c = state.content;
    var children = Array.prototype.slice.call(c.children);
    var current = null;
    var body = null;
    for (var i = 0; i < children.length; i++) {
      var el = children[i];
      var tag = el.tagName;
      if (tag === 'H2') {
        current = doc.createElement('section');
        current.className = 'mdv-section';
        body = doc.createElement('div');
        body.className = 'mdv-section-body';
        c.insertBefore(current, el);
        current.appendChild(el);
        current.appendChild(body);

        var btn = doc.createElement('button');
        btn.type = 'button';
        btn.className = 'mdv-fold';
        btn.appendChild(makeChevron());
        el.insertBefore(btn, el.firstChild);
        updateFoldButton(current);
      } else if (tag === 'H1') {
        current = null;
        body = null;
      } else if (body) {
        body.appendChild(el);
      }
    }
  }

  function unfoldAncestors(el) {
    var s = el && el.closest ? el.closest('.mdv-section.mdv-folded') : null;
    while (s) {
      setFolded(s, false);
      s = s.parentElement ? s.parentElement.closest('.mdv-section.mdv-folded') : null;
    }
  }

  // ------------------------------------------------------------ appearance

  function normalizeColumns(v) {
    if (v === undefined || v === null) {
      return null;
    }
    if (typeof v === 'number') {
      return v <= 0 ? 'auto' : Math.min(3, Math.max(1, Math.round(v)));
    }
    var s = String(v).toLowerCase();
    if (s === 'auto' || s === '0') return 'auto';
    if (s === 'one' || s === '1') return 1;
    if (s === 'two' || s === '2') return 2;
    if (s === 'three' || s === '3') return 3;
    return 'auto';
  }

  function setVar(name, value) {
    if (value === undefined || value === null || name.indexOf('--') !== 0) {
      return;
    }
    if (typeof value === 'number' && LENGTH_VARS[name]) {
      value = value + 'px';
    }
    if (name === '--mdv-columns') {
      return; // computed here from the column mode
    }
    root.style.setProperty(name, String(value));
  }

  function pick(obj, names) {
    for (var i = 0; i < names.length; i++) {
      if (obj && obj[names[i]] !== undefined && obj[names[i]] !== null) {
        return obj[names[i]];
      }
    }
    return undefined;
  }

  function setAppearance(a) {
    if (typeof a === 'string') {
      try { a = JSON.parse(a); } catch (e) { return; }
    }
    if (!a || typeof a !== 'object') {
      return;
    }

    var vars = a.vars || a.cssVars || a.variables;
    if (vars) {
      Object.keys(vars).forEach(function (k) { setVar(k, vars[k]); });
    }
    Object.keys(a).forEach(function (k) {
      if (k.indexOf('--') === 0) {
        setVar(k, a[k]);
      }
    });

    var theme = pick(a, ['theme']);
    var isDark = pick(a, ['isDark']);
    if (theme === undefined && isDark !== undefined) {
      theme = isDark ? 'dark' : 'light';
    }
    if (theme !== undefined) {
      root.setAttribute('data-theme', String(theme).toLowerCase() === 'dark' ? 'dark' : 'light');
    }

    var wrap = pick(a, ['wrap', 'wrapCodeLines', 'wrapCode']);
    if (wrap !== undefined) {
      root.setAttribute('data-wrap', wrap ? 'true' : 'false');
    }

    var remote = pick(a, ['loadRemoteContent', 'loadRemote']);
    if (remote !== undefined) {
      root.setAttribute('data-remote', remote ? 'true' : 'false');
    }

    if (typeof a.density === 'string') {
      root.setAttribute('data-density', a.density.toLowerCase());
    }

    var density = a.density && typeof a.density === 'object' ? a.density : (a.metrics || {});
    var maxCols = pick(a, ['maxColumns']);
    if (maxCols === undefined) maxCols = pick(density, ['maxColumns']);
    var two = pick(a, ['twoColumnMinWidth']);
    if (two === undefined) two = pick(density, ['twoColumnMinWidth']);
    var three = pick(a, ['threeColumnMinWidth']);
    if (three === undefined) three = pick(density, ['threeColumnMinWidth']);
    if (maxCols !== undefined) state.maxColumns = Math.max(1, Math.min(3, Number(maxCols) || 1));
    if (two !== undefined) state.twoMin = Number(two) || state.twoMin;
    if (three !== undefined) state.threeMin = Number(three) || state.threeMin;

    var cols = normalizeColumns(pick(a, ['columns', 'columnMode', 'columnsMode']));
    if (cols !== null) {
      state.columnsMode = cols;
    }

    applyColumns();
  }

  function computeColumns() {
    var w = window.innerWidth;
    var n;
    if (state.columnsMode === 'auto') {
      n = 1;
      if (state.maxColumns >= 2 && w >= state.twoMin) n = 2;
      if (state.maxColumns >= 3 && w >= state.threeMin) n = 3;
    } else {
      n = state.columnsMode;
      // Never squeeze a column below a readable width.
      var fit = Math.max(1, Math.floor((w + 40) / (300 + 40)));
      n = Math.min(n, fit);
    }
    return n;
  }

  function scrollProgress() {
    var s = scroller();
    if (!s) return 0;
    var max = state.paged ? s.scrollWidth - s.clientWidth : s.scrollHeight - s.clientHeight;
    var pos = state.paged ? s.scrollLeft : s.scrollTop;
    return max > 0 ? pos / max : 0;
  }

  function applyColumns() {
    var n = computeColumns();
    var paged = n > 1;
    if (n === state.columns && paged === state.paged && root.hasAttribute('data-columns')) {
      return;
    }
    var progress = scrollProgress();
    var anchor = state.currentHeading ? doc.getElementById(state.currentHeading) : null;

    state.columns = n;
    state.paged = paged;
    root.style.setProperty('--mdv-columns', String(n));
    root.setAttribute('data-columns', String(n));
    root.setAttribute('data-paged', paged ? 'true' : 'false');

    // Keep the reader's place across a layout switch.
    requestAnimationFrame(function () {
      if (!state.content) return;
      if (anchor) {
        revealElement(anchor, 'start', true);
      } else {
        var s = scroller();
        if (state.paged) {
          s.scrollLeft = snapToPage(progress * (s.scrollWidth - s.clientWidth));
        } else {
          s.scrollTop = progress * (s.scrollHeight - s.clientHeight);
        }
      }
      observeHeadings();
    });
  }

  // ------------------------------------------------------- paged movement

  function pageMetrics() {
    var c = state.content;
    var cs = getComputedStyle(c);
    var inner = c.clientWidth - px(cs.paddingLeft) - px(cs.paddingRight);
    var gap = px(cs.columnGap) || 40;
    var colW = (inner - gap * (state.columns - 1)) / state.columns;
    return { page: inner + gap, column: colW + gap };
  }

  function snapToPage(x) {
    var m = pageMetrics();
    return m.column > 0 ? Math.round(x / m.column) * m.column : x;
  }

  function moveBy(dx, smooth) {
    var c = state.content;
    var target = snapToPage(c.scrollLeft + dx);
    c.scrollTo({ left: target, behavior: smooth ? scrollBehavior() : 'auto' });
  }

  // Scrolls so the element is visible; in paged mode lands on a whole column boundary.
  function revealElement(el, block, instant) {
    if (!el) return;
    unfoldAncestors(el);
    var behavior = instant ? 'auto' : scrollBehavior();
    if (!state.paged) {
      el.scrollIntoView({ block: block || 'center', inline: 'nearest', behavior: behavior });
      return;
    }
    var c = state.content;
    var cRect = c.getBoundingClientRect();
    var r = el.getBoundingClientRect();
    var cs = getComputedStyle(c);
    var x = c.scrollLeft + (r.left - cRect.left) - px(cs.paddingLeft);
    var m = pageMetrics();
    var colIndex = Math.floor((x + 1) / m.column);
    var target = colIndex * m.column;
    // If the element already sits in a visible column, do not move.
    var visibleStart = c.scrollLeft;
    var visibleEnd = c.scrollLeft + m.page - 1;
    if (block !== 'start' && target >= visibleStart - 1 && target <= visibleEnd) {
      return;
    }
    c.scrollTo({ left: target, behavior: behavior });
  }

  // ------------------------------------------------------------ scroll spy

  var io = null;

  function collectHeadings() {
    state.headings = Array.prototype.slice.call(state.content.querySelectorAll('h1[id], h2[id], h3[id], h4[id]'));
  }

  var reportHeading = throttle(function () {
    var id = null;
    var hs = state.headings;
    for (var i = 0; i < hs.length; i++) {
      if (state.visibleHeadings.has(hs[i])) {
        id = hs[i].id;
        break;
      }
    }
    if (!id) {
      // Nothing visible: the last heading that lies before the viewport.
      for (var j = hs.length - 1; j >= 0; j--) {
        var r = hs[j].getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue; // folded away
        if (state.paged ? r.right < 0 : r.bottom < 0) {
          id = hs[j].id;
          break;
        }
      }
    }
    if (id && id !== state.currentHeading) {
      state.currentHeading = id;
      post({ type: 'heading', id: id });
    }
  }, 120);

  function observeHeadings() {
    if (!('IntersectionObserver' in window)) return;
    if (io) io.disconnect();
    state.visibleHeadings.clear();
    io = new IntersectionObserver(function (entries) {
      for (var i = 0; i < entries.length; i++) {
        if (entries[i].isIntersecting) {
          state.visibleHeadings.add(entries[i].target);
        } else {
          state.visibleHeadings.delete(entries[i].target);
        }
      }
      reportHeading();
    }, { root: null, rootMargin: '0px 0px -40% 0px', threshold: 0 });
    for (var k = 0; k < state.headings.length; k++) {
      io.observe(state.headings[k]);
    }
  }

  // ------------------------------------------------------------------ find

  function escapeRegExp(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  function clearFind() {
    var hits = state.content ? state.content.querySelectorAll('mark.mdv-hit') : [];
    var parents = new Set();
    for (var i = 0; i < hits.length; i++) {
      var m = hits[i];
      var p = m.parentNode;
      if (!p) continue;
      while (m.firstChild) p.insertBefore(m.firstChild, m);
      p.removeChild(m);
      parents.add(p);
    }
    parents.forEach(function (p) { p.normalize(); });
    state.hits = [];
    state.hitIndex = -1;
    state.lastQuery = '';
  }

  function textNodesFor(rootEl) {
    var walker = doc.createTreeWalker(rootEl, NodeFilter.SHOW_TEXT, {
      acceptNode: function (node) {
        if (!node.nodeValue) return NodeFilter.FILTER_REJECT;
        var p = node.parentElement;
        if (!p) return NodeFilter.FILTER_REJECT;
        if (p.closest('script, style, noscript, template, .mdv-fold, svg')) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      }
    });
    var nodes = [];
    var n;
    while ((n = walker.nextNode())) nodes.push(n);
    return nodes;
  }

  function buildHits(query) {
    var re = new RegExp(escapeRegExp(query), 'gi');
    var nodes = textNodesFor(state.content);
    var hits = [];
    var LIMIT = 5000;
    for (var i = 0; i < nodes.length && hits.length < LIMIT; i++) {
      var node = nodes[i];
      var text = node.nodeValue;
      re.lastIndex = 0;
      var match;
      var ranges = [];
      while ((match = re.exec(text)) && hits.length + ranges.length < LIMIT) {
        if (match[0].length === 0) { re.lastIndex++; continue; }
        ranges.push([match.index, match.index + match[0].length]);
      }
      if (!ranges.length) continue;

      // Rebuild the text node as text + <mark> pieces (DOM nodes only, never innerHTML).
      var frag = doc.createDocumentFragment();
      var pos = 0;
      for (var r = 0; r < ranges.length; r++) {
        var s = ranges[r][0];
        var e = ranges[r][1];
        if (s > pos) frag.appendChild(doc.createTextNode(text.slice(pos, s)));
        var mark = doc.createElement('mark');
        mark.className = 'mdv-hit';
        mark.appendChild(doc.createTextNode(text.slice(s, e)));
        frag.appendChild(mark);
        hits.push(mark);
        pos = e;
      }
      if (pos < text.length) frag.appendChild(doc.createTextNode(text.slice(pos)));
      node.parentNode.replaceChild(frag, node);
    }
    return hits;
  }

  // Index of the first hit at or after the reading position (or before it when searching backwards).
  function firstHitFromView(backwards) {
    var hits = state.hits;
    for (var i = 0; i < hits.length; i++) {
      var r = hits[i].getBoundingClientRect();
      if (r.width === 0 && r.height === 0) continue;
      var inView = state.paged ? r.right >= 0 : r.bottom >= 0;
      if (inView) {
        return backwards ? (i - 1 + hits.length) % hits.length : i;
      }
    }
    return backwards ? hits.length - 1 : 0;
  }

  function setCurrentHit(index) {
    var hits = state.hits;
    if (state.hitIndex >= 0 && hits[state.hitIndex]) {
      hits[state.hitIndex].classList.remove('current');
    }
    state.hitIndex = index;
    var m = hits[index];
    if (m) {
      m.classList.add('current');
      revealElement(m, 'center', false);
    }
  }

  function find(query, backwards) {
    query = query == null ? '' : String(query);
    if (!state.content) return;
    if (!query) {
      clearFind();
      post({ type: 'find', current: 0, total: 0 });
      return;
    }

    if (query === state.lastQuery && state.hits.length && state.hits[0].isConnected) {
      var n = state.hits.length;
      var next = backwards ? (state.hitIndex - 1 + n) % n : (state.hitIndex + 1) % n;
      setCurrentHit(next);
    } else {
      clearFind();
      state.lastQuery = query;
      state.hits = buildHits(query);
      if (state.hits.length) {
        setCurrentHit(firstHitFromView(!!backwards));
      }
    }

    post({
      type: 'find',
      current: state.hits.length ? state.hitIndex + 1 : 0,
      total: state.hits.length
    });
  }

  // ---------------------------------------------------- anchors and folds

  function byId(id) {
    if (!id) return null;
    id = String(id);
    if (id.charAt(0) === '#') id = id.slice(1);
    var el = doc.getElementById(id);
    if (!el) {
      try { el = doc.getElementById(decodeURIComponent(id)); } catch (e) { el = null; }
    }
    if (!el) {
      var named = doc.getElementsByName(id);
      el = named && named.length ? named[0] : null;
    }
    return el;
  }

  function scrollToAnchor(id) {
    var el = byId(id);
    if (!el) return false;
    revealElement(el, 'start', false);
    if (/^H[1-6]$/.test(el.tagName) && el.id && el.id !== state.currentHeading) {
      state.currentHeading = el.id;
      post({ type: 'heading', id: el.id });
    }
    return true;
  }

  function foldAll(folded) {
    var sections = state.content ? state.content.querySelectorAll('.mdv-section') : [];
    for (var i = 0; i < sections.length; i++) setFolded(sections[i], folded);
  }

  // --------------------------------------------------------- scroll position

  function getScrollY() {
    var s = scroller();
    if (!s) return 0;
    return state.paged ? s.scrollLeft : s.scrollTop;
  }

  function setScrollY(y) {
    var s = scroller();
    if (!s) return;
    var v = Number(y) || 0;
    if (state.paged) {
      s.scrollLeft = v;
    } else {
      s.scrollTop = v;
    }
  }

  var reportScroll = debounce(function () {
    post({ type: 'scroll', y: getScrollY() });
  }, 150);

  // ---------------------------------------------------------------- events

  function onClick(e) {
    if (e.defaultPrevented || e.button > 1) return;
    var target = e.target;

    var fold = target.closest && target.closest('.mdv-fold');
    if (fold) {
      e.preventDefault();
      var section = fold.closest('.mdv-section');
      if (section) setFolded(section, !section.classList.contains('mdv-folded'));
      return;
    }

    var a = target.closest && target.closest('a[href]');
    if (a) {
      e.preventDefault();
      var raw = a.getAttribute('href') || '';
      if (raw.charAt(0) === '#') {
        scrollToAnchor(raw.slice(1));
      }
      post({ type: 'link', href: a.href });
      return;
    }

    if (target.tagName === 'IMG' && e.button === 0) {
      target.classList.toggle('mdv-expanded');
    }
  }

  function onKeyDown(e) {
    if (e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey || isEditable(e.target)) return;
    var key = e.key;

    // The shell owns these; XAML accelerators never fire while the WebView has focus.
    if (key === 'F11' || key === 'Escape') {
      e.preventDefault();
      post({ type: 'key', key: key });
      return;
    }

    var lh = px(getComputedStyle(doc.body).lineHeight) || 20;

    if (!state.paged) {
      if (key === 'j' || key === 'k') {
        e.preventDefault();
        var s = scroller();
        s.scrollBy({ top: (key === 'j' ? 1 : -1) * lh * 3, behavior: 'auto' });
      }
      return; // Space, arrows, PgUp/PgDn, Home/End scroll natively.
    }

    // Paged multi-column mode: move sideways by column or page.
    var m = pageMetrics();
    var handled = true;
    switch (key) {
      case 'j': case 'ArrowDown': case 'ArrowRight':
        moveBy(m.column, true); break;
      case 'k': case 'ArrowUp': case 'ArrowLeft':
        moveBy(-m.column, true); break;
      case ' ': case 'PageDown':
        moveBy((e.shiftKey && key === ' ') ? -m.page : m.page, true); break;
      case 'PageUp':
        moveBy(-m.page, true); break;
      case 'Home':
        state.content.scrollTo({ left: 0, behavior: scrollBehavior() }); break;
      case 'End':
        state.content.scrollTo({ left: state.content.scrollWidth, behavior: scrollBehavior() }); break;
      default:
        handled = false;
    }
    if (handled) e.preventDefault();
  }

  // Vertical wheel turns into sideways movement in paged mode.
  var wheelAccum = 0;
  var flushWheel = debounce(function () { wheelAccum = 0; }, 200);

  function onWheel(e) {
    if (!state.paged || e.ctrlKey) return;
    if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) return; // native horizontal gesture
    var c = state.content;
    var dy = e.deltaMode === 1 ? e.deltaY * 16 : (e.deltaMode === 2 ? e.deltaY * c.clientHeight : e.deltaY);
    e.preventDefault();
    wheelAccum += dy;
    var m = pageMetrics();
    var step = m.column / 3;
    if (Math.abs(wheelAccum) >= step) {
      var cols = wheelAccum > 0 ? 1 : -1;
      wheelAccum = 0;
      moveBy(cols * m.column, true);
    }
    flushWheel();
  }

  function onScroll() {
    reportScroll();
  }

  var onResize = debounce(function () {
    applyColumns();
  }, 100);

  // ------------------------------------------------------------------ init

  function init() {
    if (state.ready) return;
    ensureContent();

    if (!root.hasAttribute('data-theme')) root.setAttribute('data-theme', 'light');
    if (!root.hasAttribute('data-wrap')) root.setAttribute('data-wrap', 'false');

    if (window.hljs && typeof window.hljs.highlightAll === 'function') {
      try { window.hljs.highlightAll(); } catch (e) { /* highlighting is cosmetic */ }
    }

    wrapTables();
    buildSections();
    collectHeadings();

    doc.addEventListener('click', onClick, true);
    doc.addEventListener('auxclick', function (e) {
      if (e.target.closest && e.target.closest('a[href]')) e.preventDefault();
    }, true);
    doc.addEventListener('keydown', onKeyDown);
    window.addEventListener('wheel', onWheel, { passive: false });
    window.addEventListener('scroll', onScroll, { passive: true });
    state.content.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onResize);
    doc.addEventListener('error', function (e) {
      var t = e.target;
      if (t && t.tagName === 'IMG') {
        t.classList.add('mdv-blocked');
        if (!t.alt) t.alt = 'Image not loaded';
      }
    }, true);

    // Apply the initial appearance embedded by the host page (<script type="application/json" id="mdv-appearance">).
    var initial = doc.getElementById('mdv-appearance');
    if (initial && initial.textContent) {
      setAppearance(initial.textContent);
    } else {
      applyColumns();
    }

    observeHeadings();
    state.ready = true;
    post({ type: 'ready' });
  }

  // ------------------------------------------------------------ public API

  window.mdv = {
    setAppearance: setAppearance,
    scrollToAnchor: scrollToAnchor,
    find: find,
    clearFind: function () {
      clearFind();
      post({ type: 'find', current: 0, total: 0 });
    },
    collapseAll: function () { foldAll(true); },
    expandAll: function () { foldAll(false); },
    getScrollY: getScrollY,
    setScrollY: setScrollY
  };

  if (doc.readyState === 'loading') {
    doc.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
