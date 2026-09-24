/* Build Beacon site: tabs, theme, and the Discount Matrix tool.
 *
 * The matrix is drawn from rules text in the mod's rules-file format, parsed the way the mod parses it
 * (BuildBeacon/RulesFile.cs and DiscountRules.cs): the defaults come from the page's matrix-data block, a server's
 * rules from the "Your server's rules" panel. Names match ignoring case, spaces and underscores; "*" as a material means
 * every material; a trophy in the boss rules is a boss trophy, and its creature rules do nothing.
 */
(function () {
  'use strict';

  var data = JSON.parse(document.getElementById('matrix-data').textContent);
  var ANY = '*';

  function load(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
  function store(k, v) { try { if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (e) { /* storage off */ } }
  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function $(id) { return document.getElementById(id); }

  // ---- Theme ----
  var systemDark = window.matchMedia('(prefers-color-scheme: dark)');
  function isDark() {
    var root = document.documentElement;
    return root.getAttribute('data-theme') === 'dark' || (!root.hasAttribute('data-theme') && systemDark.matches);
  }
  // The toggle's popover and label name the mode a click switches to
  function themeTip() {
    var text = 'Switch to ' + (isDark() ? 'light' : 'dark') + ' mode';
    $('theme-tip').textContent = text;
    $('theme-toggle').setAttribute('aria-label', text);
  }
  $('theme-toggle').addEventListener('click', function () {
    var next = isDark() ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    store('bb-theme', next);
    themeTip();
  });
  if (systemDark.addEventListener) systemDark.addEventListener('change', themeTip); // follows the system until a choice is made
  themeTip();

  // ---- Tabs: a hash naming a tab (#mod, #pieces, #matrix, #roadmap, #changelog) shows it; any other hash shows the
  // tab whose panel holds that element (a README heading, a piece card, a changelog version); no hash, the mod ----
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.tabs [role="tab"]'));
  var built = false;
  function anchorTarget(hash) {
    return hash.length > 1 ? document.getElementById(decodeURIComponent(hash.slice(1))) : null;
  }
  function tabFor(hash) {
    for (var i = 0; i < tabs.length; i++) if (tabs[i].getAttribute('href') === hash) return tabs[i].id;
    var el = anchorTarget(hash), panel = el && el.closest('[role="tabpanel"]');
    return panel ? panel.getAttribute('aria-labelledby') : 'tab-mod';
  }
  function showTab() {
    var current = tabFor(location.hash);
    tabs.forEach(function (tab) {
      var on = tab.id === current;
      tab.setAttribute('aria-selected', on ? 'true' : 'false');
      tab.tabIndex = on ? 0 : -1;
      $(tab.getAttribute('aria-controls')).hidden = !on;
      if (on) { // phones: bring the tab into the (sideways-scrolling) tab row, without moving the page
        var row = tab.parentNode;
        row.scrollLeft = tab.offsetLeft - row.offsetLeft - (row.clientWidth - tab.offsetWidth) / 2;
      }
    });
    if (current === 'tab-matrix' && !built) { built = true; init(); }
    // An anchor inside a panel that was hidden when the browser tried to scroll to it: scroll now it shows
    var target = tabs.some(function (t) { return t.getAttribute('href') === location.hash; }) ? null : anchorTarget(location.hash);
    if (target) target.scrollIntoView();
  }
  window.addEventListener('hashchange', showTab);
  document.querySelector('.tabs').addEventListener('keydown', function (e) {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    var i = tabs.indexOf(document.activeElement);
    if (i < 0) return;
    var next = tabs[(i + (e.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
    next.focus();
    location.hash = next.getAttribute('href');
    e.preventDefault();
  });
  // showTab() runs at the end of this file: opening #matrix directly builds the matrix, which needs everything below.

  // ---- Rules, read as the mod reads them ----
  function key(s) { return s.replace(/[\s_]/g, '').toLowerCase(); }

  function parseRules(bossText, mobText) {
    var boss = new Map(), mob = new Map(), stackable = new Set(), names = new Map(), rejected = [];
    var counts = { boss: 0, mob: 0 };
    function mat(text) {
      var k = text === ANY ? ANY : key(text);
      if (!names.has(k)) names.set(k, k === ANY ? 'All materials' : (data.prettyNames[k] || text));
      return k;
    }
    function lines(text, which, each) {
      String(text || '').split('\n').forEach(function (raw) {
        var line = raw.trim();
        var hash = line.indexOf('#');
        if (hash >= 0) line = line.slice(0, hash).trim();
        if (!line) return;
        var parts = line.split('|').map(function (p) { return p.trim(); });
        if (!each(parts)) rejected.push(which + ': ' + raw.trim());
      });
    }
    lines(bossText, 'boss', function (p) {
      if (p.length !== 2 || !p[0] || !p[1]) return false;
      var t = key(p[0]);
      if (!boss.has(t)) boss.set(t, { id: p[0], mats: [] });
      var m = mat(p[1]);
      if (boss.get(t).mats.indexOf(m) < 0) boss.get(t).mats.push(m);
      counts.boss++;
      return true;
    });
    lines(mobText, 'creature', function (p) {
      if (p.length === 2 && p[0] && p[1].toLowerCase() === 'stackable') { stackable.add(key(p[0])); return true; }
      if (p.length !== 3 || !p[0] || !p[1] || !/^[+]?\d+$/.test(p[2])) return false;
      var level = parseInt(p[2], 10);
      if (level < 1) return false;
      var t = key(p[0]);
      if (!mob.has(t)) mob.set(t, { id: p[0], mats: new Map() });
      var m = mat(p[1]);
      var mats = mob.get(t).mats;
      mats.set(m, (mats.get(m) || 0) + level); // two lines for one trophy and material add up, as in the mod
      counts.mob++;
      return true;
    });
    boss.forEach(function (_, t) { mob.delete(t); }); // a boss trophy's creature rules do nothing
    return { boss: boss, mob: mob, stackable: stackable, names: names, rejected: rejected, counts: counts };
  }

  function parseLevels(text) {
    var parts = String(text || '').split(',');
    var out = [];
    for (var i = 0; i < parts.length; i++) {
      var v = parseFloat(parts[i].trim());
      if (!isFinite(v)) return null;
      out.push(Math.max(0, Math.min(100, v)));
    }
    return out.length ? out : null;
  }

  // ---- State ----
  function parseBossPct(text) {
    var v = parseFloat(String(text || '').trim());
    return isFinite(v) ? Math.max(50, Math.min(100, v)) : null; // the mod's range for BossPercent
  }

  var state = {
    rules: null, levels: data.levelPercents.slice(), bossPct: data.bossPercent, source: 'defaults',
    biomes: [], groups: [], cols: [], freedBy: new Map(),
    owned: new Set(), q: '', biome: '', onlyRules: false, balance: false,
  };

  function useRules(bossText, mobText, levels, bossPct, source) {
    state.rules = parseRules(bossText, mobText);
    state.levels = levels || data.levelPercents.slice();
    state.bossPct = bossPct || data.bossPercent;
    state.source = source;
    // The catalogue, plus any trophy the rules name that it does not know
    var known = new Set();
    state.biomes = data.biomes.map(function (b) {
      return {
        name: b.name,
        trophies: b.trophies.map(function (t) {
          known.add(key(t.id));
          return { id: t.id, key: key(t.id), name: t.name, boss: t.boss, rare: t.rare, note: t.note, inferred: t.inferred };
        }),
      };
    });
    var extra = [];
    [state.rules.boss, state.rules.mob].forEach(function (table) {
      table.forEach(function (e, t) {
        if (known.has(t)) return;
        known.add(t);
        extra.push({ id: e.id, key: t, name: e.id, boss: state.rules.boss.has(t), rare: false, note: 'Not a trophy this page knows', inferred: false });
      });
    });
    if (extra.length) state.biomes.push({ name: 'Other trophies', trophies: extra });

    // Columns: what each boss frees, in rules order, then what only creature trophies discount, in biome order
    var byKey = new Map();
    state.biomes.forEach(function (b) { b.trophies.forEach(function (t) { byKey.set(t.key, t); }); });
    var seen = new Set();
    state.groups = [];
    state.freedBy = new Map();
    state.rules.boss.forEach(function (e, t) {
      var label = byKey.has(t) ? byKey.get(t).name : e.id;
      var cols = e.mats.filter(function (m) { return !seen.has(m); });
      cols.forEach(function (m) { seen.add(m); });
      e.mats.forEach(function (m) { if (!state.freedBy.has(m)) state.freedBy.set(m, label); });
      if (cols.length) state.groups.push({ label: 'Freed by ' + label, cols: cols });
    });
    var creature = [];
    state.biomes.forEach(function (b) {
      b.trophies.forEach(function (t) {
        var e = state.rules.mob.get(t.key);
        if (e) e.mats.forEach(function (_, m) { if (!seen.has(m)) { seen.add(m); creature.push(m); } });
      });
    });
    if (creature.length) state.groups.push({ label: 'Creature discounts only', cols: creature });
    state.cols = [];
    state.groups.forEach(function (g) { state.cols = state.cols.concat(g.cols); });
    state.byKey = byKey;

    // Owned trophies that the new rules still know
    state.owned.forEach(function (t) { if (!byKey.has(t)) state.owned.delete(t); });
  }

  // ---- Levels to percentages ----
  function top() { return state.levels.length; }
  function pct(level) { return level <= 0 ? 0 : state.levels[Math.min(level, top()) - 1]; }
  // Orange, yellow, green (lv2 to lv4) spread over the levels there are, as in the mod's panel: the top level is green
  function lvClass(level) {
    var l = Math.max(1, Math.min(level, top()));
    return 'lv' + (top() > 1 ? 2 + Math.round((l - 1) * 2 / (top() - 1)) : 4);
  }
  function bossFree() { return state.bossPct >= 100; }
  function bossLabel() { return bossFree() ? 'Free' : '-' + state.bossPct + '%'; }
  function mult(p) {
    if (p >= 100) return '∞';
    return (100 / (100 - p)).toFixed(2).replace(/\.?0+$/, '') + 'x';
  }
  function levelText(level) { return 'Lv ' + Math.min(level, top()) + (level > top() ? '+' : ''); }

  // What one trophy does to one material: 'free', a level, or 0
  function cell(t, m) {
    var b = state.rules.boss.get(t.key);
    if (b && (b.mats.indexOf(m) >= 0)) return 'free';
    var e = state.rules.mob.get(t.key);
    return e && e.mats.has(m) ? e.mats.get(m) : 0;
  }

  // A set of trophies in one beacon: free if any boss frees it (or frees everything), else the levels added up
  function combined(keys, m) {
    var level = 0;
    for (var i = 0; i < keys.length; i++) {
      var b = state.rules.boss.get(keys[i]);
      if (b && (b.mats.indexOf(m) >= 0 || b.mats.indexOf(ANY) >= 0)) return 'free';
      var e = state.rules.mob.get(keys[i]);
      if (e) level += (e.mats.get(m) || 0) + (m === ANY ? 0 : (e.mats.get(ANY) || 0));
    }
    return level;
  }

  // The highest creature level a material can reach: every creature trophy with a rule for it (or for every material)
  // counted once and added up, capped at the top level; a stackable one reaches the top, as in the mod (MaxMobLevel).
  function maxLevel(m) {
    var level = 0, sources = 0, stack = false;
    state.rules.mob.forEach(function (e, t) {
      var lv = (e.mats.get(m) || 0) + (m === ANY ? 0 : (e.mats.get(ANY) || 0));
      if (!lv) return;
      sources++;
      level += lv;
      if (state.rules.stackable.has(t)) stack = true;
    });
    return { level: stack ? top() : Math.min(level, top()), sources: sources };
  }

  // ---- Facts and legend ----
  function renderFacts() {
    var trophies = 0, withRules = 0;
    state.biomes.forEach(function (b) {
      b.trophies.forEach(function (t) {
        trophies++;
        if (state.rules.boss.has(t.key) || state.rules.mob.has(t.key)) withRules++;
      });
    });
    $('mx-facts').innerHTML =
      '<span><b>' + trophies + '</b> trophies</span><span><b>' + withRules + '</b> with rules</span>' +
      '<span><b>' + state.cols.length + '</b> materials</span><span><b>' + state.rules.counts.boss + '</b> boss rules</span>' +
      '<span><b>' + state.rules.counts.mob + '</b> creature rules</span>' +
      '<span class="src">' + (state.source === 'server' ? "Your server's rules" : 'Default rules') + '</span>';
    var legend = state.levels.map(function (p, i) {
      return '<span class="chip"><span class="sw ' + lvClass(i + 1) + '">' + (i + 1) + '</span>' + p + '% &middot; ' + mult(p) + '</span>';
    }).join('');
    $('mx-legend').innerHTML = legend + '<span class="chip"><span class="sw free">' + bossLabel() + '</span>boss' +
      (bossFree() ? '' : ' &middot; ' + mult(state.bossPct)) + '</span>';
  }

  // ---- The table ----
  function balanceTargets() {
    var construction = new Set(data.construction), powerful = new Set(data.powerful), rareDrops = new Set(data.rareDrops);
    var all = new Map(), common = new Map();
    state.cols.forEach(function (m) { all.set(m, 0); common.set(m, 0); });
    state.biomes.forEach(function (b) {
      b.trophies.forEach(function (t) {
        var e = state.rules.mob.get(t.key);
        if (!e) return;
        e.mats.forEach(function (lv, m) {
          if (!all.has(m)) return;
          all.set(m, all.get(m) + lv);
          if (!t.rare) common.set(m, common.get(m) + lv);
        });
      });
    });
    function target(m) {
      if (!all.get(m)) return [null, null];
      if (construction.has(m)) return [2, 3];
      if (powerful.has(m)) return [null, null]; // capped instead
      if (rareDrops.has(m)) return [null, 1];
      return [1, null];
    }
    // The most levels a material may reach with every creature trophy: less for powerful ones, else the top level
    function cap(m) { return powerful.has(m) ? data.powerfulCap : top(); }
    return { all: all, common: common, target: target, cap: cap, construction: construction };
  }

  function renderTable() {
    var q = state.q.trim().toLowerCase();
    var names = state.rules.names;
    var matMatch = q ? state.cols.filter(function (m) { return names.get(m).toLowerCase().indexOf(q) >= 0; }) : [];
    var cols = matMatch.length ? matMatch : state.cols;
    var colSet = new Set(cols);

    var body = [];
    var shown = 0;
    state.biomes.forEach(function (b) {
      if (state.biome && b.name !== state.biome) return;
      var rows = [];
      b.trophies.forEach(function (t) {
        var has = state.rules.boss.has(t.key) || state.rules.mob.has(t.key);
        if (state.onlyRules && !has) return;
        if (q) {
          var nameHit = t.name.toLowerCase().indexOf(q) >= 0 || t.id.toLowerCase().indexOf(q) >= 0;
          var ruleHit = matMatch.length && cols.some(function (m) { return cell(t, m); });
          if (!nameHit && !ruleHit) return;
        }
        var owned = state.owned.has(t.key);
        var isBoss = state.rules.boss.has(t.key) || (t.boss && !state.rules.mob.has(t.key));
        var tag = isBoss ? '<span class="tag">Boss</span>' : t.rare ? '<span class="tag rare">Rare</span>' : '';
        var dag = t.inferred ? '<span class="dag" title="Biome inferred">&dagger;</span>' : '';
        var r = '<tr class="t' + (owned ? ' owned' : '') + '"><th class="rowhead" scope="row"><button type="button" class="pick" data-t="' + esc(t.key) +
          '" aria-pressed="' + owned + '" title="' + (owned ? 'Remove from' : 'Add to') + ' your trophies"><span class="box" aria-hidden="true"></span>' +
          '<span class="txt"><span class="n">' + esc(t.name) + dag + tag + '</span><span class="p">' + esc(t.id) + '</span></span></button></th>';
        var mats = 0, levels = 0;
        cols.forEach(function (m) {
          var c = cell(t, m);
          if (c === 'free') { r += '<td class="c cell free" title="' + esc(t.name) + ': ' + esc(names.get(m)) + ' ' + (bossFree() ? 'free' : state.bossPct + '% off') + '">' + bossLabel() + '</td>'; mats++; }
          else if (c) { r += '<td class="c cell ' + lvClass(c) + '" title="' + esc(t.name) + ': ' + esc(names.get(m)) + ' +' + c + '">' + c + '</td>'; mats++; levels += c; }
          else r += '<td class="c"></td>';
        });
        r += '<td class="sum">' + (mats || '') + '</td><td class="sum">' + (levels || '') + '</td><td class="note">' +
          esc(t.note) + (state.rules.stackable.has(t.key) ? (t.note ? '; ' : '') + 'Stackable' : '') + '</td></tr>';
        rows.push(r);
      });
      if (!rows.length) return;
      shown += rows.length;
      body.push('<tr class="biome"><th scope="rowgroup">' + esc(b.name) + '</th><td colspan="' + (cols.length + 3) + '"></td></tr>');
      body = body.concat(rows);
    });

    if (!shown) {
      $('mx-scroller').innerHTML = '<p class="empty">No trophies match' + (q ? ' &ldquo;' + esc(state.q.trim()) + '&rdquo;' : '') + '.</p>';
      return;
    }

    var h = ['<table class="mx" id="mx-table"><colgroup><col>'];
    cols.forEach(function () { h.push('<col>'); });
    h.push('<col><col><col></colgroup><thead><tr class="groups"><th class="corner" rowspan="2">Trophy<br>item name</th>');
    state.groups.forEach(function (g) {
      var n = g.cols.filter(function (m) { return colSet.has(m); }).length;
      if (n) h.push('<th colspan="' + n + '">' + esc(g.label) + '</th>');
    });
    h.push('<th class="sumh" rowspan="2">Mats</th><th class="sumh" rowspan="2">Levels</th><th class="sumh" rowspan="2" style="text-align:left;padding-left:12px">Notes</th></tr><tr class="mats">');
    cols.forEach(function (m) {
      var by = state.freedBy.get(m);
      h.push('<th class="m" title="' + esc(names.get(m)) + (by ? ' (freed by ' + esc(by) + ')' : '') + '"><span>' + esc(names.get(m)) + '</span></th>');
    });
    h.push('</tr></thead><tbody>' + body.join('') + '</tbody><tfoot>');

    if (state.balance) {
      var bt = balanceTargets();
      var f1 = '', f2 = '', f3 = '';
      cols.forEach(function (m) {
        var tg = bt.target(m);
        if (tg[0] === null && tg[1] === null) f1 += '<td class="c tgt"></td>';
        else f1 += '<td class="c tgt' + (bt.construction.has(m) ? ' build' : '') + '">' + (tg[0] === null ? '-' : tg[0]) + '/' + (tg[1] === null ? '-' : tg[1]) + '</td>';
        [[bt.common.get(m), tg[0], 'from common trophies'], [bt.all.get(m), tg[1], 'with rare trophies too']].forEach(function (x, i) {
          var lv = x[0], want = x[1];
          var td = !lv ? '<td class="c">&middot;</td>' :
            '<td class="c cell ' + lvClass(lv) + ((want !== null && lv < want) || lv > bt.cap(m) ? ' miss' : '') + '" title="' + esc(names.get(m)) + ': level ' + lv + ' ' + x[2] +
            (want !== null ? ', target ' + want : '') + (lv > bt.cap(m) ? ', cap ' + bt.cap(m) : '') + '">' + lv + '</td>';
          if (i === 0) f2 += td; else f3 += td;
        });
      });
      h.push('<tr class="f1"><th class="rowhead">Target: common / rare</th>' + f1 + '<td class="sum"></td><td class="sum"></td><td class="note">Construction 2/3; powerful at most 2; rare-only drops -/1; the rest 1/-</td></tr>' +
        '<tr class="f2"><th class="rowhead">From common trophies</th>' + f2 + '<td class="sum"></td><td class="sum"></td><td class="note">One of each common creature trophy</td></tr>' +
        '<tr class="f3"><th class="rowhead">With rare trophies</th>' + f3 + '<td class="sum"></td><td class="sum"></td><td class="note">One of every creature trophy</td></tr>');
    }

    // Always last: how far creature trophies alone can take each material
    var fm = '';
    cols.forEach(function (m) {
      var mx = maxLevel(m);
      if (!mx.level) { fm += '<td class="c" title="' + esc(names.get(m)) + ': no creature trophy discounts it">&middot;</td>'; return; }
      var p = pct(mx.level);
      fm += '<td class="c cell ' + lvClass(mx.level) + '" title="' + esc(names.get(m)) + ': up to level ' + mx.level + ' (' + p + '% &middot; ' + mult(p) +
        ') from ' + mx.sources + (mx.sources === 1 ? ' creature trophy' : ' creature trophies') + '">' + mx.level + '</td>';
    });
    h.push('<tr class="fmax"><th class="rowhead">Max level</th>' + fm + '<td class="sum"></td><td class="sum"></td><td class="note">Every creature trophy together, up to level ' + top() + '</td></tr>');
    h.push('</tfoot></table>');
    $('mx-scroller').innerHTML = h.join('');
  }

  // Highlight the column under the pointer
  var hot = null;
  $('mx-scroller').addEventListener('mouseover', function (e) {
    var c = e.target.closest('td,th');
    var table = $('mx-table');
    if (!c || !table || !table.contains(c)) return;
    var row = c.parentElement;
    var idx = (row.classList.contains('biome') || row.classList.contains('groups')) ? -1 : c.cellIndex;
    var col = idx > 0 ? table.querySelectorAll('colgroup col')[idx] : null;
    if (col === hot) return;
    if (hot) hot.classList.remove('hot');
    hot = col;
    if (hot) hot.classList.add('hot');
  });
  $('mx-scroller').addEventListener('mouseleave', function () { if (hot) { hot.classList.remove('hot'); hot = null; } });

  // ---- Your trophies ----
  function renderOwned() {
    var keys = Array.from(state.owned);
    $('owned-clear').hidden = !keys.length;
    $('owned-hint').textContent = keys.length
      ? keys.length + (keys.length === 1 ? ' trophy' : ' trophies') + ' in one beacon. Each creature trophy counts once; levels stop at level ' + top() + '.'
      : 'Tick trophies in the table below (the box beside each name) to add them up the way a beacon does.';
    $('owned-list').innerHTML = keys.map(function (k) {
      var t = state.byKey.get(k);
      return '<button type="button" data-t="' + esc(k) + '" title="Remove">' + esc(t ? t.name : k) + '</button>';
    }).join('');
    var res = [];
    state.cols.forEach(function (m) {
      var c = combined(keys, m);
      if (c) res.push({ m: m, c: c });
    });
    res.sort(function (a, b) {
      if ((a.c === 'free') !== (b.c === 'free')) return a.c === 'free' ? -1 : 1;
      if (a.c !== 'free' && a.c !== b.c) return b.c - a.c;
      return state.rules.names.get(a.m).localeCompare(state.rules.names.get(b.m));
    });
    $('owned-results').innerHTML = res.map(function (r) {
      var nm = esc(state.rules.names.get(r.m));
      if (r.c === 'free') return '<div class="res"><span class="sw free">' + (bossFree() ? 'Free' : 'Boss') + '</span><span class="nm">' + nm + '</span><span class="pc">' +
        (bossFree() ? 'costs 0' : state.bossPct + '% &middot; ' + mult(state.bossPct)) + '</span></div>';
      var p = pct(r.c);
      return '<div class="res"><span class="sw ' + lvClass(r.c) + '">' + levelText(r.c).replace('Lv ', '') + '</span><span class="nm">' + nm +
        '</span><span class="pc">' + p + '% &middot; ' + mult(p) + '</span></div>';
    }).join('');
  }

  function toggleOwned(k) {
    if (state.owned.has(k)) state.owned.delete(k); else state.owned.add(k);
    store('bb-owned', JSON.stringify(Array.from(state.owned)));
    renderTable();
    renderOwned();
  }

  // ---- Server rules panel ----
  function reportRules() {
    var r = state.rules;
    var lines = [
      (state.source === 'server' ? "Using your server's rules: " : 'Using the default rules: ') +
      r.counts.boss + ' boss rules, ' + r.counts.mob + ' creature rules' +
      (r.stackable.size ? ', ' + r.stackable.size + ' stackable' : '') + '; levels ' + state.levels.join(' / ') + '%; bosses ' +
      (bossFree() ? 'free' : state.bossPct + '% off') + '.',
    ];
    var html = esc(lines[0]);
    if (r.rejected.length) {
      html += '\n<span class="bad">Ignored ' + r.rejected.length + ' line' + (r.rejected.length === 1 ? '' : 's') + ' the mod would not read:</span>\n' +
        r.rejected.slice(0, 20).map(esc).join('\n') + (r.rejected.length > 20 ? '\n...' : '');
    }
    $('rules-report').innerHTML = html;
  }

  function fillRuleInputs(saved) {
    $('rules-boss').value = saved ? saved.boss : data.bossRules;
    $('rules-mob').value = saved ? saved.mob : data.mobRules;
    $('rules-levels').value = (saved && saved.levels ? saved.levels : data.levelPercents).join(', ');
    $('rules-boss-pct').value = String(saved && saved.bossPct ? saved.bossPct : data.bossPercent);
  }

  function refresh() {
    renderFacts();
    renderTable();
    renderOwned();
    reportRules();
  }

  function init() {
    var saved = null;
    try { saved = JSON.parse(load('bb-rules') || 'null'); } catch (e) { saved = null; }
    if (saved && typeof saved.boss === 'string' && typeof saved.mob === 'string') useRules(saved.boss, saved.mob, saved.levels, saved.bossPct, 'server');
    else { saved = null; useRules(data.bossRules, data.mobRules, null, null, 'defaults'); }
    fillRuleInputs(saved);
    if (saved) $('server').open = true;

    try { JSON.parse(load('bb-owned') || '[]').forEach(function (k) { if (state.byKey.has(k)) state.owned.add(k); }); } catch (e) { /* ignore */ }
    state.onlyRules = load('bb-only-rules') === '1';
    state.balance = load('bb-balance') === '1';
    $('mx-only-rules').checked = state.onlyRules;
    $('mx-balance').checked = state.balance;
    document.querySelector('.balance-only').hidden = !state.balance;

    var sel = $('mx-biome');
    sel.innerHTML = '<option value="">All biomes</option>' + state.biomes.map(function (b) {
      return '<option>' + esc(b.name) + '</option>';
    }).join('');

    $('mx-search').addEventListener('input', function (e) { state.q = e.target.value; renderTable(); });
    sel.addEventListener('change', function (e) { state.biome = e.target.value; renderTable(); });
    $('mx-only-rules').addEventListener('change', function (e) {
      state.onlyRules = e.target.checked; store('bb-only-rules', e.target.checked ? '1' : '0'); renderTable();
    });
    $('mx-balance').addEventListener('change', function (e) {
      state.balance = e.target.checked; store('bb-balance', e.target.checked ? '1' : '0');
      document.querySelector('.balance-only').hidden = !state.balance;
      renderTable();
    });
    $('mx-scroller').addEventListener('click', function (e) {
      var b = e.target.closest('.pick');
      if (b) toggleOwned(b.getAttribute('data-t'));
    });
    $('owned-list').addEventListener('click', function (e) {
      var b = e.target.closest('button[data-t]');
      if (b) toggleOwned(b.getAttribute('data-t'));
    });
    $('owned-clear').addEventListener('click', function () {
      state.owned.clear(); store('bb-owned', null); renderTable(); renderOwned();
    });

    $('rules-apply').addEventListener('click', function () {
      var levels = parseLevels($('rules-levels').value);
      var bossPct = parseBossPct($('rules-boss-pct').value);
      var boss = $('rules-boss').value, mob = $('rules-mob').value;
      useRules(boss, mob, levels, bossPct, 'server');
      store('bb-rules', JSON.stringify({ boss: boss, mob: mob, levels: state.levels, bossPct: state.bossPct }));
      $('rules-boss-pct').value = String(state.bossPct);
      refreshBiomes();
      refresh();
      if (!levels) $('rules-report').innerHTML += '\n<span class="bad">Level percents did not read as a list of numbers; using ' + esc(state.levels.join(', ')) + '.</span>';
      if (!bossPct) $('rules-report').innerHTML += '\n<span class="bad">Boss percent did not read as a number; using ' + state.bossPct + '.</span>';
    });
    $('rules-reset').addEventListener('click', function () {
      store('bb-rules', null);
      useRules(data.bossRules, data.mobRules, null, null, 'defaults');
      fillRuleInputs(null);
      refreshBiomes();
      refresh();
    });
    $('rules-files').addEventListener('change', function (e) {
      Array.prototype.forEach.call(e.target.files, function (f) {
        var reader = new FileReader();
        reader.onload = function () {
          var n = f.name.toLowerCase();
          var target = n.indexOf('boss') >= 0 ? 'rules-boss' : n.indexOf('mob') >= 0 || n.indexOf('creature') >= 0 ? 'rules-mob' : null;
          if (target) $(target).value = String(reader.result);
          else $('rules-report').textContent = f.name + ': not sure which rules this is; paste it into the right box.';
        };
        reader.readAsText(f);
      });
      e.target.value = '';
    });

    refresh();
  }

  function refreshBiomes() {
    var sel = $('mx-biome');
    var names = state.biomes.map(function (b) { return b.name; });
    if (state.biome && names.indexOf(state.biome) < 0) state.biome = '';
    sel.innerHTML = '<option value="">All biomes</option>' + names.map(function (n) {
      return '<option' + (n === state.biome ? ' selected' : '') + '>' + esc(n) + '</option>';
    }).join('');
  }

  showTab();
})();
