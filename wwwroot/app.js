/* Leonio Group IMS Portal - browser client */
'use strict';

const S = { token: null, me: null, level: 0, settings: null, users: null, taskCount: 0 };
try { S.token = sessionStorage.getItem('ims.token'); } catch (e) { /* storage unavailable */ }

/* ---------------- utilities ---------------- */

const $ = (sel, root) => (root || document).querySelector(sel);
const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));
const esc = s => s == null ? '' : String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const app = () => $('#app');

async function api(method, path, body) {
  const opts = { method, headers: {} };
  if (S.token) opts.headers.Authorization = 'Bearer ' + S.token;
  if (body !== undefined) { opts.headers['Content-Type'] = 'application/json'; opts.body = JSON.stringify(body); }
  const res = await fetch(path, opts);
  let data = null;
  try { data = await res.json(); } catch (e) { /* empty */ }
  if (res.status === 401 && path !== '/api/login') { logoutLocal(); throw new Error((data && data.error) || 'Please sign in.'); }
  if (!res.ok) throw new Error((data && data.error) || ('Request failed (' + res.status + ')'));
  return data;
}

function toast(msg) {
  const t = $('#toast');
  t.textContent = msg;
  t.classList.add('show');
  clearTimeout(toast._t);
  toast._t = setTimeout(() => t.classList.remove('show'), 2600);
}

function setToken(t) {
  S.token = t;
  try { t ? sessionStorage.setItem('ims.token', t) : sessionStorage.removeItem('ims.token'); } catch (e) { /* ignore */ }
}
function logoutLocal() { setToken(null); S.me = null; renderLogin(); }

const codeName = (list, code) => { const x = (list || []).find(i => i.Code === code); return x ? x.Name : code; };
const opts = (list, sel, empty) => (empty !== undefined ? `<option value="">${esc(empty)}</option>` : '') +
  list.map(o => `<option value="${esc(o.value)}" ${String(o.value) === String(sel) ? 'selected' : ''}>${esc(o.label)}</option>`).join('');
const codeOpts = (list, sel, empty) => opts(list.map(x => ({ value: x.Code, label: x.Name })), sel, empty);
const fmtDate = d => {
  if (!d) return '';
  const p = d.substring(0, 10).split('-');
  if (p.length !== 3) return d;
  const m = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][+p[1] - 1];
  return `${+p[2]} ${m} ${p[0]}` + (d.length > 10 ? ' ' + d.substring(11) : '');
};

const REV_STATUS = {
  Draft: ['Draft', ''], InReview: ['In approval', 'amber'], Approved: ['Approved – to publish', 'blue'],
  Rejected: ['Returned for correction', 'red'], Effective: ['Effective', 'green'], Superseded: ['Superseded', ''],
  Withdrawn: ['Withdrawn', ''], Obsolete: ['Obsolete', 'red']
};
const chip = (text, cls) => `<span class="chip ${cls || ''}">${esc(text)}</span>`;
const revChip = s => { const x = REV_STATUS[s] || [s, '']; return chip(x[0], x[1]); };
const CAPA_CLS = { 'Open': 'red', 'In Progress': 'amber', 'Pending Verification': 'blue', 'Closed': 'green' };
const capaChip = c => chip(c.status, CAPA_CLS[c.status]) + (c.overdue ? ' ' + chip('Overdue', 'red') : '');
const AUDIT_CLS = { 'Planned': 'blue', 'In Progress': 'amber', 'Completed': 'green' };
const FIND_CLS = { 'Major NC': 'red', 'Minor NC': 'amber', 'Observation': 'blue', 'Opportunity for Improvement': '' };
const reviewChip = d => d.reviewState === 'Overdue' ? chip('Review overdue', 'red') : d.reviewState === 'Due soon' ? chip('Review due soon', 'amber') : '';

/* Minimal, safe markdown: headings, lists, tables, bold, italics, paragraphs. Escapes everything first. */
function md(src) {
  const lines = esc(src || '').replace(/\r/g, '').split('\n');
  const inline = s => s.replace(/\*\*(.+?)\*\*/g, '<b>$1</b>').replace(/(^|[^*])\*([^*]+)\*/g, '$1<i>$2</i>');
  let out = '', list = null, para = [], i = 0;
  const flushPara = () => { if (para.length) { out += '<p>' + inline(para.join(' ')) + '</p>'; para = []; } };
  const closeList = () => { if (list) { out += `</${list}>`; list = null; } };
  while (i < lines.length) {
    const l = lines[i];
    let m;
    if (/^\s*$/.test(l)) { flushPara(); closeList(); i++; continue; }
    if ((m = l.match(/^(#{1,3})\s+(.*)$/))) { flushPara(); closeList(); const n = m[1].length + 1; out += `<h${n}>${inline(m[2])}</h${n}>`; i++; continue; }
    if (/^\s*\|/.test(l)) {
      flushPara(); closeList();
      const rows = [];
      while (i < lines.length && /^\s*\|/.test(lines[i])) { rows.push(lines[i]); i++; }
      const cells = r => r.trim().replace(/^\||\|$/g, '').split('|').map(c => c.trim());
      let html = '<table>';
      rows.forEach((r, idx) => {
        if (/^\s*\|?\s*:?-{2,}/.test(r)) return;
        const tag = idx === 0 && rows[1] && /-{2,}/.test(rows[1]) ? 'th' : 'td';
        html += '<tr>' + cells(r).map(c => `<${tag}>${inline(c)}</${tag}>`).join('') + '</tr>';
      });
      out += html + '</table>';
      continue;
    }
    if ((m = l.match(/^\s*[-*]\s+(.*)$/))) { flushPara(); if (list !== 'ul') { closeList(); out += '<ul>'; list = 'ul'; } out += `<li>${inline(m[1])}</li>`; i++; continue; }
    if ((m = l.match(/^\s*\d+[.)]\s+(.*)$/))) { flushPara(); if (list !== 'ol') { closeList(); out += '<ol>'; list = 'ol'; } out += `<li>${inline(m[1])}</li>`; i++; continue; }
    closeList(); para.push(l); i++;
  }
  flushPara(); closeList();
  return out || '<p class="muted">No content.</p>';
}

/* ---------------- modal ---------------- */

function modal({ title, body, submit = 'Save', danger = false, wide = false, onSubmit, onOpen }) {
  const bg = document.createElement('div');
  bg.className = 'modal-bg';
  bg.innerHTML = `<form class="modal" style="${wide ? 'width:min(820px,100%)' : ''}" novalidate>
    <div class="hd"><h2>${esc(title)}</h2><button type="button" class="x" aria-label="Close">×</button></div>
    <div class="bd"><div class="error" hidden></div>${body}</div>
    <div class="ft"><button type="button" class="cancel">Cancel</button>${submit ? `<button type="submit" class="${danger ? 'danger solid' : 'primary'}">${esc(submit)}</button>` : ''}</div>
  </form>`;
  document.body.appendChild(bg);
  const form = $('form', bg), err = $('.error', bg);
  const close = () => bg.remove();
  $('.x', bg).onclick = close; $('.cancel', bg).onclick = close;
  bg.addEventListener('mousedown', e => { if (e.target === bg) close(); });
  form.onsubmit = async e => {
    e.preventDefault();
    err.hidden = true;
    const btn = $('button[type=submit]', form);
    if (btn) btn.disabled = true;
    try {
      const data = formData(form);
      await onSubmit(data, form);
      close();
    } catch (ex) {
      err.textContent = ex.message; err.hidden = false;
      if (btn) btn.disabled = false;
    }
  };
  const first = $('input:not([type=hidden]),select,textarea', form);
  if (first) first.focus();
  if (onOpen) onOpen(form);
  return close;
}

function formData(form) {
  const o = {};
  $$('input,select,textarea', form).forEach(el => {
    if (!el.name || el.disabled) return;
    if (el.type === 'checkbox') o[el.name] = el.checked;
    else if (el.type !== 'file') o[el.name] = el.value;
  });
  return o;
}

const field = (label, input, opt = {}) =>
  `<label class="f ${opt.full ? 'full' : ''}">${esc(label)}${opt.req ? ' <span style="color:var(--red)">*</span>' : ''}${input}${opt.hint ? `<span class="hint">${esc(opt.hint)}</span>` : ''}</label>`;
const textIn = (name, val, extra) => `<input type="text" name="${name}" value="${esc(val || '')}" ${extra || ''}>`;
const dateIn = (name, val) => `<input type="date" name="${name}" value="${esc(val || '')}">`;
const area = (name, val, rows) => `<textarea name="${name}" rows="${rows || 3}">${esc(val || '')}</textarea>`;

function confirmBox(title, text, label, danger) {
  return new Promise(resolve => {
    let ok = false;
    const close = modal({ title, body: `<p style="margin:0">${text}</p>`, submit: label, danger, onSubmit: async () => { ok = true; } });
    const obs = new MutationObserver(() => { if (!document.body.contains($('.modal-bg'))) { obs.disconnect(); resolve(ok); } });
    obs.observe(document.body, { childList: true });
    void close;
  });
}

async function users() {
  if (!S.users) S.users = await api('GET', '/api/users');
  return S.users;
}
const userOpts = (list, sel, minLevel, empty) => {
  const lvl = { Employee: 1, Contributor: 2, Controller: 3, Admin: 4 };
  return opts(list.filter(u => u.active && lvl[u.role] >= (minLevel || 1)).map(u => ({ value: u.id, label: `${u.name} — ${u.position || u.role}` })), sel, empty);
};

function readFile(input) {
  return new Promise((resolve, reject) => {
    const f = input && input.files && input.files[0];
    if (!f) return resolve(null);
    if (f.size > 25 * 1024 * 1024) return reject(new Error('Attachments are limited to 25 MB.'));
    const r = new FileReader();
    r.onload = () => resolve({ fileData: r.result, fileName: f.name, fileType: f.type || 'application/octet-stream' });
    r.onerror = () => reject(new Error('Could not read the file.'));
    r.readAsDataURL(f);
  });
}

/* ---------------- login & shell ---------------- */

function renderLogin(message) {
  app().innerHTML = `<div class="login">
    <div class="side">
      <div class="brand"><div class="logo">L</div><div>LEONIO GROUP<small>IMS PORTAL</small></div></div>
      <div>
        <div style="text-transform:uppercase;letter-spacing:.12em;opacity:.7;font-size:12px">Integrated Management System</div>
        <h1>One controlled source for every policy and procedure.</h1>
        <ul>
          <li>Find the current approved version of any procedure</li>
          <li>Review, approve and publish controlled documents</li>
          <li>Acknowledge new and revised procedures</li>
          <li>Manage internal audits, nonconformities and CAPA</li>
        </ul>
      </div>
      <div class="small" style="opacity:.6">ISO 9001 · ISO 14001 · ISO 45001</div>
    </div>
    <div class="formside">
      <form id="loginForm" class="stack">
        <div><h1>Sign in</h1><div class="muted" style="margin-top:4px">Use your Leonio Group IMS account.</div></div>
        <div class="error" ${message ? '' : 'hidden'}>${esc(message || '')}</div>
        ${field('Username', '<input type="text" name="username" autocomplete="username" required>')}
        ${field('Password', '<input type="password" name="password" autocomplete="current-password" required>')}
        <button class="primary" style="width:100%;justify-content:center;padding:10px">Sign in</button>
        <div class="small muted">Forgot your password? Contact the IMS administrator.</div>
      </form>
    </div></div>`;
  const f = $('#loginForm');
  $('input', f).focus();
  f.onsubmit = async e => {
    e.preventDefault();
    const err = $('.error', f);
    try {
      const r = await api('POST', '/api/login', formData(f));
      setToken(r.token);
      await boot();
    } catch (ex) { err.textContent = ex.message; err.hidden = false; }
  };
}

async function boot() {
  if (!S.token) return renderLogin();
  try {
    const me = await api('GET', '/api/me');
    S.me = me.user; S.level = me.level; S.settings = me.settings; S.users = null;
  } catch (e) { return; }
  if (!location.hash) location.hash = '#/';
  route();
}

const NAV = [
  ['Portal', null, 0],
  ['Home', '/', 1, '⌂'],
  ['Document Library', '/library', 1, '▤'],
  ['My Tasks', '/tasks', 1, '✓', 'tasks'],
  ['Management System', null, 2],
  ['Document Control', '/manage', 2, '✎'],
  ['Internal Audits', '/audits', 2, '⚑'],
  ['CAPA', '/capas', 1, '↻'],
  ['Reports', '/reports', 2, '▥'],
  ['Administration', null, 3],
  ['Users & Access', '/users', 4, '☺'],
  ['Audit Trail', '/log', 3, '☰'],
];

function shell(path) {
  const navHtml = NAV.filter(n => S.level >= n[2]).map(n => {
    if (!n[1]) return `<div class="sect">${esc(n[0])}</div>`;
    const active = n[1] === '/' ? path === '/' : path.startsWith(n[1]) || (n[1] === '/library' && path.startsWith('/doc')) ||
      (n[1] === '/audits' && path.startsWith('/audit')) || (n[1] === '/capas' && path.startsWith('/capa')) || (n[1] === '/manage' && path.startsWith('/new'));
    const badge = n[4] === 'tasks' && S.taskCount ? `<span class="badge">${S.taskCount}</span>` : '';
    return `<a href="#${n[1]}" class="${active ? 'active' : ''}"><span style="width:16px;text-align:center;opacity:.7">${n[3]}</span>${esc(n[0])}${badge}</a>`;
  }).join('');
  if (!$('.topbar')) {
    app().innerHTML = `<header class="topbar">
        <a class="brand" href="#/"><div class="logo">L</div><div>LEONIO GROUP<small>IMS PORTAL</small></div></a>
        <form class="topsearch" id="topSearch"><input type="search" name="q" placeholder="Search policies, procedures and work instructions…" aria-label="Search documents"></form>
        <div class="spacer"></div>
        <div class="usermenu"><a class="who" href="#/profile" style="color:#fff">${esc(S.me.name)}<small>${esc(S.me.position || S.me.role)} · ${esc(S.me.role)}</small></a>
        <button id="logoutBtn" class="small">Sign out</button></div>
      </header>
      <div class="shell"><nav class="sidebar" id="nav"></nav><main class="main" id="main"></main></div>`;
    $('#topSearch').onsubmit = e => { e.preventDefault(); const q = $('#topSearch input').value.trim(); location.hash = '#/library' + (q ? '?q=' + encodeURIComponent(q) : ''); };
    $('#logoutBtn').onclick = async () => { try { await api('POST', '/api/logout'); } catch (e) { /* ignore */ } logoutLocal(); location.hash = ''; };
  }
  $('#nav').innerHTML = navHtml;
}

const main = () => $('#main');

async function refreshTaskCount() {
  try { const t = await api('GET', '/api/tasks'); S.taskCount = t.total; shell(currentPath()); } catch (e) { /* ignore */ }
}

/* ---------------- router ---------------- */

function currentPath() { return (location.hash.slice(1) || '/').split('?')[0]; }
function currentQuery() { return new URLSearchParams((location.hash.split('?')[1]) || ''); }

const ROUTES = [
  [/^\/$/, viewHome],
  [/^\/library$/, viewLibrary],
  [/^\/doc\/(\d+)$/, viewDoc],
  [/^\/doc\/(\d+)\/edit$/, viewEditor],
  [/^\/new$/, viewEditor],
  [/^\/tasks$/, viewTasks],
  [/^\/manage$/, viewManage],
  [/^\/audits$/, viewAudits],
  [/^\/audit\/(\d+)$/, viewAudit],
  [/^\/capas$/, viewCapas],
  [/^\/capa\/(\d+)$/, viewCapa],
  [/^\/reports$/, viewReports],
  [/^\/users$/, viewUsers],
  [/^\/log$/, viewLog],
  [/^\/profile$/, viewProfile],
];

/* Page loads run one at a time; if the address changes mid-load, the latest page is rendered last. */
let routing = false, routeAgain = false;
async function route() {
  if (routing) { routeAgain = true; return; }
  routing = true;
  try { do { routeAgain = false; await routeOnce(); } while (routeAgain); }
  finally { routing = false; }
}

async function routeOnce() {
  if (!S.me) return;
  let path = currentPath();
  if (S.me.mustChangePassword && path !== '/profile') { location.hash = '#/profile'; return; }
  shell(path);
  const r = ROUTES.find(x => x[0].test(path));
  main().innerHTML = '<div class="muted">Loading…</div>';
  window.scrollTo(0, 0);
  try {
    if (!r) throw new Error('Page not found.');
    await r[1](...path.match(r[0]).slice(1));
  } catch (e) {
    main().innerHTML = `<div class="error">${esc(e.message)}</div>`;
  }
  if (!routeAgain) refreshTaskCount();
}
window.addEventListener('hashchange', route);

/* ---------------- shared bits ---------------- */

function docRows(list, opt = {}) {
  if (!list.length) return `<div class="empty">${esc(opt.empty || 'No documents found.')}</div>`;
  return `<div class="tablewrap"><table class="list"><thead><tr>
    <th>Document No.</th><th>Title</th><th>Type</th><th>Applies to</th><th>Rev</th><th>${opt.manage ? 'Status' : 'Effective'}</th>${opt.manage ? '<th>Owner</th><th>Next review</th>' : '<th></th>'}
    </tr></thead><tbody>${list.map(d => `<tr class="click" data-href="#/doc/${d.id}">
      <td class="mono nowrap">${esc(d.docNo)}</td>
      <td><a href="#/doc/${d.id}"><b>${esc(d.title)}</b></a><div class="small muted">${esc(d.deptName)}${d.classification === 'Restricted' ? ' · Restricted' : ''}</div></td>
      <td class="nowrap">${esc(d.typeName)}</td>
      <td class="nowrap">${esc(d.buName)}</td>
      <td>${esc(d.rev || '—')}</td>
      ${opt.manage
        ? `<td>${d.status === 'Effective' ? chip('Effective', 'green') : d.status === 'Obsolete' ? chip('Obsolete', 'red') : chip('Not yet issued')}
            ${d.workingStatus ? `<div style="margin-top:4px">${revChip(d.workingStatus)} <span class="small muted">Rev ${esc(d.workingRev)}</span></div>` : ''}</td>
           <td class="nowrap">${esc(d.ownerName)}</td><td class="nowrap">${fmtDate(d.nextReviewDate)} ${reviewChip(d)}</td>`
        : `<td class="nowrap">${fmtDate(d.effectiveDate)}</td><td class="nowrap">${d.inAudience ? (d.acknowledged ? chip('Acknowledged', 'green') : chip('Please acknowledge', 'amber')) : ''}</td>`}
    </tr>`).join('')}</tbody></table></div>`;
}

function bindRowLinks(root) {
  $$('tr[data-href]', root || main()).forEach(tr => tr.addEventListener('click', e => { if (e.target.closest('a,button')) return; location.hash = tr.dataset.href; }));
}

function historyList(items) {
  if (!items || !items.length) return '<div class="empty">No activity recorded.</div>';
  return `<ul class="timeline">${items.map(l => `<li><div><b>${esc(l.Action)}</b> · ${esc(l.UserName)}</div>
    ${l.Detail ? `<div class="small">${esc(l.Detail)}</div>` : ''}<div class="when">${fmtDate(l.At)}</div></li>`).join('')}</ul>`;
}

/* ---------------- home ---------------- */

async function viewHome() {
  const [docs, recent, tasks] = await Promise.all([
    api('GET', '/api/documents'), api('GET', '/api/documents?sort=recent&limit=6'), api('GET', '/api/tasks')]);
  const icons = { POL: 'POL', PRO: 'SOP', WI: 'WI', FRM: 'FRM', MAN: 'MAN' };
  const plurals = { POL: 'Policies', PRO: 'Procedures', WI: 'Work Instructions', FRM: 'Forms & Templates', MAN: 'Manuals' };
  const blurbs = { POL: 'Group-wide policies', PRO: 'Standard operating procedures', WI: 'Operational instructions', FRM: 'Controlled forms', MAN: 'Manuals and handbooks' };
  const work = tasks.toApprove.length + tasks.toPublish.length + tasks.toVerify.length + tasks.myCapas.length + tasks.reviewsDue.length;
  main().innerHTML = `
    <section class="hero">
      <div class="eyebrow">Leonio Group · Integrated Management System</div>
      <h1>IMS Portal</h1>
      <p>Your centralized source of approved company policies and procedures.</p>
      <form id="heroSearch"><input type="search" name="q" placeholder="Search policies, procedures and work instructions…" aria-label="Search"><button>Search</button></form>
    </section>
    <div class="cats">${S.settings.docTypes.map(t => `<a class="cat" href="#/library?type=${t.Code}">
      <div class="ic">${icons[t.Code] || t.Code}</div><b>${esc(plurals[t.Code] || t.Name)}</b>
      <span>${esc(blurbs[t.Code] || '')} · ${docs.filter(d => d.type === t.Code).length}</span></a>`).join('')}</div>
    ${work ? `<div class="notice blue" style="margin-bottom:18px"><b>You have ${work} item${work > 1 ? 's' : ''} in your work queue.</b>
      <span class="muted">${[tasks.toApprove.length && tasks.toApprove.length + ' to approve', tasks.toPublish.length && tasks.toPublish.length + ' to publish',
        tasks.myCapas.length && tasks.myCapas.length + ' corrective action(s)', tasks.toVerify.length && tasks.toVerify.length + ' CAPA to verify',
        tasks.reviewsDue.length && tasks.reviewsDue.length + ' review(s) due'].filter(Boolean).join(' · ')}</span>
      <span class="spacer"></span><a class="btn small" href="#/tasks">Open My Tasks</a></div>` : ''}
    <div class="grid2">
      <div class="card"><div class="hd"><h2>Awaiting your acknowledgement</h2><span class="spacer"></span>${tasks.pendingAcks.length ? chip(tasks.pendingAcks.length, 'amber') : ''}</div>
        <div class="bd flush doclist">${tasks.pendingAcks.length ? tasks.pendingAcks.slice(0, 6).map(d => docItem(d, 'Rev ' + d.rev + ' · effective ' + fmtDate(d.effectiveDate))).join('')
          : '<div class="empty">You are up to date. All procedures that apply to you are acknowledged.</div>'}</div></div>
      <div class="card"><div class="hd"><h2>Recently published</h2><span class="spacer"></span><a href="#/library" class="small">View library</a></div>
        <div class="bd flush doclist">${recent.length ? recent.map(d => docItem(d, fmtDate(d.effectiveDate) + ' · Rev ' + d.rev)).join('') : '<div class="empty">Nothing published yet.</div>'}</div></div>
    </div>`;
  $('#heroSearch').onsubmit = e => { e.preventDefault(); const q = $('#heroSearch input').value.trim(); location.hash = '#/library' + (q ? '?q=' + encodeURIComponent(q) : ''); };
}

const docItem = (d, sub) => `<a class="item" href="#/doc/${d.id}"><div class="t"><b>${esc(d.title)}</b><span class="small muted mono">${esc(d.docNo)}</span> <span class="small muted">· ${esc(sub)}</span></div>
  ${d.acknowledged ? chip('Acknowledged', 'green') : d.inAudience ? chip('To acknowledge', 'amber') : ''}</a>`;

/* ---------------- library ---------------- */

async function viewLibrary() {
  const q = currentQuery();
  const params = new URLSearchParams();
  ['q', 'type', 'bu', 'dept', 'mine'].forEach(k => { if (q.get(k)) params.set(k, q.get(k)); });
  const list = await api('GET', '/api/documents?' + params);
  const st = S.settings;
  main().innerHTML = `
    <div class="pagehead"><div><h1>Document Library</h1><div class="sub">Current approved revisions only. Printed copies are uncontrolled.</div></div></div>
    <form class="card" id="filters" style="margin-bottom:16px"><div class="bd row">
      <input type="search" name="q" value="${esc(q.get('q') || '')}" placeholder="Search title, number, keyword or content" style="flex:2;min-width:220px">
      <select name="type" style="flex:1;min-width:150px">${codeOpts(st.docTypes, q.get('type'), 'All types')}</select>
      <select name="bu" style="flex:1;min-width:150px">${codeOpts(st.businessUnits, q.get('bu'), 'All companies / BUs')}</select>
      <select name="dept" style="flex:1;min-width:150px">${codeOpts(st.departments, q.get('dept'), 'All departments')}</select>
      <label class="check"><input type="checkbox" name="mine" ${q.get('mine') ? 'checked' : ''}> Applies to me</label>
      <button class="primary">Search</button>
    </div></form>
    <div class="card"><div class="hd"><b>${list.length} document${list.length === 1 ? '' : 's'}</b></div><div class="bd flush">${docRows(list, { empty: 'No documents match your search.' })}</div></div>`;
  const f = $('#filters');
  f.onsubmit = e => {
    e.preventDefault();
    const d = formData(f), p = new URLSearchParams();
    Object.keys(d).forEach(k => { if (k === 'mine') { if (d.mine) p.set('mine', '1'); } else if (d[k]) p.set(k, d[k]); });
    location.hash = '#/library' + (p.toString() ? '?' + p : '');
  };
  $$('select,input[type=checkbox]', f).forEach(el => el.onchange = () => f.requestSubmit());
  bindRowLinks();
}

/* ---------------- document view ---------------- */

async function viewDoc(id) {
  const data = await api('GET', '/api/documents/' + id);
  const d = data.doc, eff = data.effective, w = data.working, p = data.perms;
  const tab = currentQuery().get('tab') || (eff ? 'doc' : 'draft');
  const tabs = [['doc', 'Current revision', !!eff], ['draft', w ? `Rev ${w.rev} ${(REV_STATUS[w.status] || [w.status])[0].toLowerCase()}` : '', !!w],
    ['history', 'Revision history', true], ['acks', 'Acknowledgements', p.manage && !!eff], ['activity', 'Activity', p.manage]].filter(t => t[2]);

  let banner = '';
  if (d.status === 'Obsolete') banner = `<div class="notice red"><b>This document is obsolete.</b> ${esc(d.obsoleteReason || '')}</div>`;
  else if (eff && d.inAudience && !d.acknowledged) banner = `<div class="notice amber"><div><b>Please read and acknowledge Rev ${esc(eff.rev)}.</b>
      <div class="small muted">Confirm that you have read and understood this ${esc(d.typeName.toLowerCase())}.</div></div><span class="spacer"></span>
      <button class="primary" id="ackBtn">I have read and understood</button></div>`;
  else if (eff && d.acknowledged) banner = `<div class="notice green"><b>✓ You acknowledged Rev ${esc(eff.rev)}</b><span class="muted">on ${fmtDate(data.myAck)}</span></div>`;

  let wbanner = '';
  if (w) {
    const who = w.status === 'InReview' ? `Submitted by ${esc(w.submittedByName)} on ${fmtDate(w.submittedAt)} · approver: ${esc(d.approverName)}`
      : w.status === 'Approved' ? `Approved by ${esc(w.reviewedByName)} on ${fmtDate(w.reviewedAt)} · waiting for the Document Controller to publish`
      : w.status === 'Rejected' ? `Returned by ${esc(w.reviewedByName)}: “${esc(w.reviewComment)}”`
      : `Draft prepared by ${esc(w.createdByName)}`;
    wbanner = `<div class="notice ${w.status === 'Rejected' ? 'red' : 'blue'}"><div><b>Rev ${esc(w.rev)} — ${(REV_STATUS[w.status] || [w.status])[0]}</b><div class="small muted">${who}</div></div>
      <span class="spacer"></span>
      ${tab !== 'draft' ? `<a class="btn small" href="#/doc/${d.id}?tab=draft">View draft</a>` : ''}
      ${p.edit ? `<a class="btn small" href="#/doc/${d.id}/edit">Edit</a>` : ''}
      ${p.submit ? `<button class="small primary" data-act="submit">Submit for approval</button>` : ''}
      ${p.approve ? `<button class="small success" data-act="approve">Approve</button><button class="small danger" data-act="reject">Return</button>` : ''}
      ${p.publish ? `<button class="small primary" data-act="publish">Publish as effective</button>` : ''}
      ${p.delete ? `<button class="small danger" data-act="discard">Discard draft</button>` : ''}
    </div>`;
  }

  const showRev = tab === 'draft' ? w : eff;
  let body = '';
  if (tab === 'doc' || tab === 'draft') body = paper(d, showRev, tab === 'draft');
  else if (tab === 'history') body = `<div class="card"><div class="bd flush tablewrap"><table class="list"><thead><tr><th>Rev</th><th>Status</th><th>Description of change</th><th>Prepared</th><th>Approved</th><th>Effective</th></tr></thead><tbody>
    ${data.revisions.map(r => `<tr><td class="mono">${esc(r.rev)}</td><td>${revChip(r.status)}</td><td>${esc(r.changeSummary || '')}</td>
      <td class="small nowrap">${esc(r.createdByName)}<div class="muted">${fmtDate((r.createdAt || '').substring(0, 10))}</div></td>
      <td class="small nowrap">${r.reviewedByName && r.status !== 'Rejected' ? esc(r.reviewedByName) + `<div class="muted">${fmtDate((r.reviewedAt || '').substring(0, 10))}</div>` : '—'}</td>
      <td class="small nowrap">${r.effectiveDate ? fmtDate(r.effectiveDate) + (r.supersededAt ? `<div class="muted">until ${fmtDate(r.supersededAt)}</div>` : '') : '—'}</td></tr>`).join('')}
    </tbody></table></div></div>`;
  else if (tab === 'acks') body = '<div class="card"><div class="bd" id="ackList">Loading…</div></div>';
  else if (tab === 'activity') body = `<div class="card"><div class="bd">${historyList(data.history)}</div></div>`;

  main().innerHTML = `
    <div class="crumbs"><a href="#/library">Document Library</a> › ${esc(d.typeName)} › <span class="mono">${esc(d.docNo)}</span></div>
    <div class="pagehead">
      <div style="flex:1;min-width:260px"><h1>${esc(d.title)}</h1>
        <div class="sub row" style="gap:8px"><span class="mono">${esc(d.docNo)}</span>${eff ? `<span>· Rev ${esc(eff.rev)}</span>` : ''}
          ${d.status === 'Effective' ? chip('Effective', 'green') : d.status === 'Obsolete' ? chip('Obsolete', 'red') : chip('Not yet issued')} ${reviewChip(d)}</div></div>
      <div class="row noprint">
        ${eff && tab === 'doc' ? '<button id="printBtn">Print</button>' : ''}
        ${p.revise ? '<button class="primary" data-act="revise">Start new revision</button>' : ''}
        ${p.obsolete ? '<button class="danger" data-act="obsolete">Make obsolete</button>' : ''}
      </div>
    </div>
    <div class="stack" style="margin-bottom:16px">${banner}${wbanner}</div>
    <div class="tabs">${tabs.map(t => `<a href="#/doc/${d.id}?tab=${t[0]}" class="${t[0] === tab ? 'active' : ''}">${esc(t[1])}</a>`).join('')}</div>
    <div class="docwrap"><div>${body}</div>
      <aside class="card meta"><div class="bd"><dl>
        <dt>Document type</dt><dd>${esc(d.typeName)}</dd>
        <dt>Applies to</dt><dd>${esc(d.buName)}</dd>
        <dt>Department / process area</dt><dd>${esc(d.deptName)}</dd>
        ${d.process ? `<dt>Related process</dt><dd>${esc(d.process)}</dd>` : ''}
        <dt>Document owner</dt><dd>${esc(d.ownerName)}</dd>
        <dt>Approver</dt><dd>${esc(d.approverName)}</dd>
        ${d.standards ? `<dt>Applicable standards</dt><dd>${esc(d.standards)}</dd>` : ''}
        <dt>Classification</dt><dd>${esc(d.classification)}</dd>
        ${eff ? `<dt>Effective date</dt><dd>${fmtDate(eff.effectiveDate)}</dd>` : ''}
        <dt>Review cycle</dt><dd>Every ${d.reviewMonths} months${d.nextReviewDate ? `<div class="small muted">Next review ${fmtDate(d.nextReviewDate)}</div>` : ''}</dd>
        ${data.ackStats ? `<dt>Acknowledgements</dt><dd>${data.ackStats.acknowledged} of ${data.ackStats.required} employees
          <div class="bar ${pctCls(data.ackStats)}" style="margin-top:6px"><i style="width:${pct(data.ackStats)}%"></i></div></dd>` : ''}
      </dl></div></aside></div>`;

  const run = async (fn, msg) => { try { await fn(); toast(msg); route(); } catch (e) { toast(e.message); } };
  const act = name => $$(`[data-act="${name}"]`).forEach(b => b.onclick = handlers[name]);
  const handlers = {
    submit: () => run(() => api('POST', `/api/revisions/${w.id}/submit`), 'Submitted for approval'),
    approve: () => modal({ title: `Approve Rev ${w.rev}`, submit: 'Approve revision',
      body: `<p style="margin-top:0">You are approving <b>${esc(d.docNo)} Rev ${esc(w.rev)}</b>. It will go to the Document Controller for publication.</p>${field('Comment (optional)', area('comment'))}`,
      onSubmit: async v => { await api('POST', `/api/revisions/${w.id}/approve`, v); toast('Revision approved'); route(); } }),
    reject: () => modal({ title: `Return Rev ${w.rev} for correction`, submit: 'Return to author', danger: true,
      body: field('What needs to change?', area('comment', '', 4), { req: true }),
      onSubmit: async v => { await api('POST', `/api/revisions/${w.id}/reject`, v); toast('Revision returned'); route(); } }),
    publish: () => modal({ title: `Publish Rev ${w.rev}`, submit: 'Publish now',
      body: `<p style="margin:0">Rev ${esc(w.rev)} becomes the effective version today${eff ? ` and Rev ${esc(eff.rev)} is superseded` : ''}. Employees who are covered by this document will be asked to acknowledge it.</p>`,
      onSubmit: async () => { await api('POST', `/api/revisions/${w.id}/publish`); toast('Published'); location.hash = `#/doc/${d.id}`; route(); } }),
    discard: async () => { if (await confirmBox('Discard draft', `Discard draft Rev ${esc(w.rev)}? This cannot be undone.`, 'Discard', true)) {
      try { const r = await api('DELETE', `/api/revisions/${w.id}`); toast('Draft discarded'); location.hash = r.docDeleted ? '#/manage' : `#/doc/${d.id}`; route(); } catch (e) { toast(e.message); } } },
    revise: () => run(async () => { await api('POST', `/api/documents/${d.id}/revise`); location.hash = `#/doc/${d.id}/edit`; }, 'New revision started'),
    obsolete: () => modal({ title: 'Make document obsolete', submit: 'Make obsolete', danger: true,
      body: `<p style="margin-top:0">The document is withdrawn from the library. Its records are kept.</p>${field('Reason', area('reason'), { req: true })}`,
      onSubmit: async v => { await api('POST', `/api/documents/${d.id}/obsolete`, v); toast('Document made obsolete'); route(); } }),
  };
  Object.keys(handlers).forEach(act);
  if ($('#ackBtn')) $('#ackBtn').onclick = () => run(() => api('POST', `/api/revisions/${eff.id}/acknowledge`), 'Acknowledgement recorded');
  if ($('#printBtn')) $('#printBtn').onclick = () => window.print();
  if (tab === 'acks') {
    const list = await api('GET', `/api/documents/${d.id}/acknowledgements`);
    const done = list.filter(x => x.ackedAt).length;
    $('#ackList').outerHTML = `<div class="hd"><b>Rev ${esc(eff.rev)}: ${done} of ${list.length} acknowledged</b><span class="spacer"></span>
      <div class="bar ${pctCls({ acknowledged: done, required: list.length })}" style="width:160px"><i style="width:${pct({ acknowledged: done, required: list.length })}%"></i></div></div>
      <div class="bd flush tablewrap"><table class="list"><thead><tr><th>Employee</th><th>Business unit</th><th>Department</th><th>Acknowledged</th></tr></thead><tbody>
      ${list.map(x => `<tr><td><b>${esc(x.name)}</b><div class="small muted">${esc(x.position || '')}</div></td><td>${esc(x.buName)}</td><td>${esc(x.deptName)}</td>
        <td>${x.ackedAt ? chip('✓ ' + fmtDate(x.ackedAt), 'green') : chip('Pending', 'amber')}</td></tr>`).join('')}</tbody></table></div>`;
  }
}

const pct = s => s.required ? Math.round(100 * s.acknowledged / s.required) : 100;
const pctCls = s => { const v = pct(s); return v < 50 ? 'low' : v < 80 ? 'mid' : ''; };

function paper(d, r, isDraft) {
  if (!r) return '<div class="card"><div class="empty">No revision to show.</div></div>';
  const printed = new Date().toLocaleString();
  return `<article class="paper">
    ${isDraft ? `<div class="notice amber" style="border-radius:var(--radius) var(--radius) 0 0;border-width:0 0 1px">DRAFT — not approved for use. ${r.changeSummary ? 'Change: ' + esc(r.changeSummary) : ''}</div>` : ''}
    <div class="ctrl"><div><div class="co">Leonio Group · ${esc(d.buName)} · ${esc(d.typeName)}</div><div class="ti">${esc(d.title)}</div></div>
      <div class="no">Doc No. <b>${esc(d.docNo)}</b><br>Revision <b>${esc(r.rev)}</b><br>${r.effectiveDate ? 'Effective <b>' + fmtDate(r.effectiveDate) + '</b>' : esc((REV_STATUS[r.status] || [r.status])[0])}</div></div>
    <div class="body">${md(r.content)}
      ${r.hasFile ? `<div class="attach"><span style="font-size:20px">📎</span><div style="flex:1"><b>${esc(r.fileName)}</b><div class="small muted">Attached controlled file</div></div>
        <a class="btn small" target="_blank" href="/api/revisions/${r.id}/file?t=${encodeURIComponent(S.token)}">Open</a></div>` : ''}
    </div>
    <div class="foot"><span>Owner: ${esc(d.ownerName)} · Approved by: ${esc(r.reviewedByName || '—')}</span>
      <span class="printonly">UNCONTROLLED WHEN PRINTED · Printed ${esc(printed)} by ${esc(S.me.name)} · Verify the current revision in the IMS Portal</span>
      <span class="noprint">The IMS Portal holds the controlled version.</span></div>
  </article>`;
}

/* ---------------- editor ---------------- */

async function viewEditor(id) {
  const st = S.settings;
  const list = await users();
  let d = null, r = null;
  if (id) {
    const data = await api('GET', '/api/documents/' + id);
    d = data.doc; r = data.working;
    if (!r || !data.perms.edit) throw new Error('There is no editable draft for this document.');
  }
  const isNew = !d;
  d = d || { classification: 'Internal', reviewMonths: 24, approverId: '', ownerId: S.me.id };
  r = r || { content: '', changeSummary: 'Initial issue' };
  main().innerHTML = `
    <div class="crumbs"><a href="#/manage">Document Control</a> › ${isNew ? 'New document' : `<a href="#/doc/${d.id}">${esc(d.docNo)}</a> › Edit Rev ${esc(r.rev)}`}</div>
    <div class="pagehead"><div><h1>${isNew ? 'New controlled document' : `Edit draft — ${esc(d.title)}`}</h1>
      <div class="sub">${isNew ? 'The document number is assigned automatically from the company, department and document type.' : `${esc(d.docNo)} · Rev ${esc(r.rev)} · ${(REV_STATUS[r.status] || [r.status])[0]}`}</div></div></div>
    ${r.status === 'Rejected' ? `<div class="notice red" style="margin-bottom:16px"><b>Returned by ${esc(r.reviewedByName)}:</b> ${esc(r.reviewComment)}</div>` : ''}
    <form id="ed" class="stack">
      <div class="error" hidden></div>
      <div class="card"><div class="hd"><h2>Document details</h2></div><div class="bd form">
        ${field('Title', textIn('title', d.title), { req: true, full: true })}
        ${field('Document type', `<select name="type" ${isNew ? '' : 'disabled'}>${codeOpts(st.docTypes, d.type, 'Select…')}</select>`, { req: true })}
        ${field('Applies to (company / BU)', `<select name="bu" ${isNew ? '' : 'disabled'}>${codeOpts(st.businessUnits, d.bu, 'Select…')}</select>`, { req: true, hint: 'Group-wide documents apply to every business unit.' })}
        ${field('Department / process area', `<select name="dept" ${isNew ? '' : 'disabled'}>${codeOpts(st.departments, d.dept, 'Select…')}</select>`, { req: true })}
        ${field('Related process', textIn('process', d.process, 'placeholder="e.g. Procure-to-Pay"'))}
        ${field('Approver', `<select name="approverId">${userOpts(list, d.approverId, 2, 'Select…')}</select>`, { req: true, hint: 'Must be someone other than the person preparing the revision.' })}
        ${S.level >= 3 ? field('Document owner', `<select name="ownerId">${userOpts(list, d.ownerId, 2)}</select>`) : ''}
        ${field('Applicable standards / clauses', textIn('standards', d.standards, 'placeholder="e.g. ISO 9001:2015 8.4"'))}
        ${field('Classification', `<select name="classification">${opts([{ value: 'Internal', label: 'Internal — all employees in scope' }, { value: 'Restricted', label: 'Restricted — department only' }], d.classification)}</select>`)}
        ${field('Review every (months)', `<input type="number" name="reviewMonths" min="1" max="60" value="${esc(d.reviewMonths)}">`)}
      </div></div>
      <div class="card"><div class="hd"><h2>Content — Rev ${esc(r.rev || '00')}</h2><span class="spacer"></span>
        <div class="tabs" style="margin:0;border:0"><a href="javascript:void 0" class="active" data-mode="write">Write</a><a href="javascript:void 0" data-mode="preview">Preview</a></div></div>
        <div class="bd stack">
          ${field('Description of change', textIn('changeSummary', r.changeSummary), { req: true, full: true, hint: 'Shown in the revision history.' })}
          <div id="writePane"><textarea name="content" class="content" placeholder="# Purpose&#10;…&#10;&#10;# Scope&#10;…&#10;&#10;# Procedure&#10;1. First step&#10;2. Second step">${esc(r.content)}</textarea>
            <div class="small muted" style="margin-top:6px">Formatting: <span class="mono"># Heading</span> · <span class="mono">## Sub-heading</span> · <span class="mono">- bullet</span> · <span class="mono">1. step</span> · <span class="mono">**bold**</span> · <span class="mono">| table | row |</span></div></div>
          <div id="previewPane" class="paper" hidden><div class="body"></div></div>
          <div>${r.hasFile ? `<div class="attach" style="margin:0 0 10px"><span>📎</span><b style="flex:1">${esc(r.fileName)}</b><label class="check"><input type="checkbox" name="removeFile"> Remove</label></div>` : ''}
            ${field(r.hasFile ? 'Replace attachment' : 'Attach the controlled file (optional)', '<input type="file" id="fileIn">', { hint: 'PDF, Word or Excel version of the document, up to 25 MB.' })}</div>
        </div></div>
      <div class="row"><button type="submit" class="primary" data-mode="save">Save draft</button>
        <button type="button" id="saveSubmit">Save and submit for approval</button><span class="spacer"></span>
        <a class="btn" href="${isNew ? '#/manage' : '#/doc/' + d.id}">Cancel</a></div>
    </form>`;

  const f = $('#ed'), err = $('.error', f);
  $$('[data-mode="write"],[data-mode="preview"]', f).forEach(a => a.onclick = () => {
    const pv = a.dataset.mode === 'preview';
    $$('[data-mode="write"],[data-mode="preview"]', f).forEach(x => x.classList.toggle('active', x === a));
    $('#writePane').hidden = pv; $('#previewPane').hidden = !pv;
    if (pv) $('#previewPane .body').innerHTML = md($('textarea[name=content]', f).value);
  });
  const save = async submit => {
    err.hidden = true;
    try {
      const body = formData(f);
      const file = await readFile($('#fileIn'));
      if (file) Object.assign(body, file);
      let docId = d.id, revId = r.id;
      if (isNew) { const res = await api('POST', '/api/documents', body); docId = res.id; revId = res.revisionId; }
      else await api('PUT', '/api/revisions/' + r.id, body);
      if (submit) {
        try { await api('POST', `/api/revisions/${revId}/submit`); toast('Saved and submitted for approval'); }
        catch (e) { toast('Saved as draft. Not submitted: ' + e.message); }
      } else toast('Draft saved');
      location.hash = `#/doc/${docId}?tab=draft`;
    } catch (e) { err.textContent = e.message; err.hidden = false; window.scrollTo(0, 0); }
  };
  f.onsubmit = e => { e.preventDefault(); save(false); };
  $('#saveSubmit').onclick = () => save(true);
}

/* ---------------- tasks ---------------- */

async function viewTasks() {
  const t = await api('GET', '/api/tasks');
  const sec = (title, items, render, empty) => `<div class="card"><div class="hd"><h2>${esc(title)}</h2><span class="spacer"></span>${items.length ? chip(items.length, 'blue') : ''}</div>
    <div class="bd flush doclist">${items.length ? items.map(render).join('') : `<div class="empty">${esc(empty)}</div>`}</div></div>`;
  const revItem = x => `<a class="item" href="#/doc/${x.doc.id}?tab=draft"><div class="t"><b>${esc(x.doc.title)}</b><span class="small muted mono">${esc(x.doc.docNo)} Rev ${esc(x.revision.rev)}</span>
    <span class="small muted"> · ${esc(x.revision.changeSummary || '')}</span></div>${revChip(x.revision.status)}</a>`;
  const capaItem = c => `<a class="item" href="#/capa/${c.id}"><div class="t"><b>${esc(c.title)}</b><span class="small muted mono">${esc(c.ref)}</span><span class="small muted"> · due ${fmtDate(c.dueDate)} · ${esc(c.ownerName)}</span></div>${capaChip(c)}</a>`;
  const parts = [sec('Procedures to acknowledge', t.pendingAcks, d => docItem(d, 'Rev ' + d.rev + ' · ' + d.typeName), 'Nothing to acknowledge.')];
  if (S.level >= 2 || t.toApprove.length) parts.push(sec('Revisions awaiting your approval', t.toApprove, revItem, 'No revisions are waiting for your approval.'));
  if (S.level >= 3) parts.push(sec('Approved revisions to publish', t.toPublish, revItem, 'Nothing waiting for publication.'));
  if (S.level >= 2) parts.push(sec('My drafts and revisions in progress', t.myDrafts, revItem, 'You have no revisions in progress.'));
  if (t.reviewsDue.length) parts.push(sec('My documents due for periodic review', t.reviewsDue, d => `<a class="item" href="#/doc/${d.id}"><div class="t"><b>${esc(d.title)}</b>
    <span class="small muted mono">${esc(d.docNo)}</span><span class="small muted"> · review by ${fmtDate(d.nextReviewDate)}</span></div>${reviewChip(d)}</a>`, ''));
  parts.push(sec('My corrective actions (CAPA)', t.myCapas, capaItem, 'No corrective actions assigned to you.'));
  if (t.toVerify.length) parts.push(sec('CAPA awaiting your verification', t.toVerify, capaItem, ''));
  if (t.myAudits.length) parts.push(sec('Audits I am leading', t.myAudits, a => `<a class="item" href="#/audit/${a.id}"><div class="t"><b>${esc(a.title)}</b>
    <span class="small muted mono">${esc(a.ref)}</span><span class="small muted"> · ${fmtDate(a.plannedDate)}</span></div>${chip(a.status, AUDIT_CLS[a.status])}</a>`, ''));
  main().innerHTML = `<div class="pagehead"><div><h1>My Tasks</h1><div class="sub">Everything that needs your attention.</div></div></div><div class="grid2">${parts.join('')}</div>`;
}

/* ---------------- document control ---------------- */

async function viewManage() {
  const q = currentQuery();
  const status = q.get('status') || '';
  const params = new URLSearchParams({ scope: 'manage' });
  if (status) params.set('status', status);
  if (q.get('q')) params.set('q', q.get('q'));
  const list = await api('GET', '/api/documents?' + params);
  const filters = [['', 'All'], ['Pending', 'Revision in progress'], ['Draft', 'Not yet issued'], ['Effective', 'Effective'], ['Obsolete', 'Obsolete']];
  main().innerHTML = `
    <div class="pagehead"><div style="flex:1"><h1>Document Control</h1><div class="sub">${S.level >= 3 ? 'All controlled documents, including drafts and obsolete documents.' : 'Documents you own, approve or have prepared.'}</div></div>
      <a class="btn primary" href="#/new">+ New document</a></div>
    <div class="row" style="margin-bottom:14px">
      <div class="tabs" style="margin:0;border:0">${filters.map(f => `<a href="#/manage${f[0] ? '?status=' + f[0] : ''}" class="${status === f[0] ? 'active' : ''}">${f[1]}</a>`).join('')}</div>
      <span class="spacer"></span><form id="ms"><input type="search" name="q" placeholder="Search…" value="${esc(q.get('q') || '')}"></form></div>
    <div class="card"><div class="bd flush">${docRows(list, { manage: true, empty: 'No documents in this view.' })}</div></div>`;
  $('#ms').onsubmit = e => { e.preventDefault(); const p = new URLSearchParams(); if (status) p.set('status', status); const v = $('#ms input').value.trim(); if (v) p.set('q', v); location.hash = '#/manage?' + p; };
  bindRowLinks();
}

/* ---------------- audits ---------------- */

function auditForm(a, list) {
  const st = S.settings;
  a = a || {};
  return `<div class="form">
    ${field('Audit title', textIn('title', a.title), { req: true, full: true })}
    ${field('Business unit', `<select name="bu">${codeOpts(st.businessUnits, a.bu, 'Select…')}</select>`, { req: true })}
    ${field('Department / process', `<select name="dept">${codeOpts(st.departments, a.dept, 'Select…')}</select>`, { req: true })}
    ${field('Standard(s)', textIn('standard', a.standard, 'placeholder="e.g. ISO 9001:2015"'))}
    ${field('Planned date', dateIn('plannedDate', a.plannedDate), { req: true })}
    ${field('Lead auditor', `<select name="leadAuditorId">${userOpts(list, a.leadAuditorId || S.me.id, 2)}</select>`, { full: true, hint: 'Auditors should be independent of the area audited.' })}
    ${field('Scope', area('scope', a.scope), { full: true })}
  </div>`;
}

async function viewAudits() {
  const list = await api('GET', '/api/audits');
  main().innerHTML = `
    <div class="pagehead"><div style="flex:1"><h1>Internal Audits</h1><div class="sub">Audit programme, findings and nonconformities.</div></div>
      <button class="primary" id="newAudit">+ Plan audit</button></div>
    <div class="card"><div class="bd flush">${list.length ? `<div class="tablewrap"><table class="list"><thead><tr><th>Ref</th><th>Audit</th><th>Area</th><th>Lead auditor</th><th>Date</th><th>Findings</th><th>Status</th></tr></thead><tbody>
      ${list.map(a => `<tr class="click" data-href="#/audit/${a.id}"><td class="mono nowrap">${esc(a.ref)}</td><td><b>${esc(a.title)}</b><div class="small muted">${esc(a.standard || '')}</div></td>
        <td>${esc(a.buName)}<div class="small muted">${esc(a.deptName)}</div></td><td>${esc(a.leadAuditorName)}</td><td class="nowrap">${fmtDate(a.plannedDate)}</td>
        <td>${a.findings}${a.ncs ? ` <span class="small muted">(${a.ncs} NC)</span>` : ''}</td><td>${chip(a.status, AUDIT_CLS[a.status])}</td></tr>`).join('')}
      </tbody></table></div>` : '<div class="empty">No audits planned yet.</div>'}</div></div>`;
  bindRowLinks();
  $('#newAudit').onclick = async () => {
    const list2 = await users();
    modal({ title: 'Plan internal audit', body: auditForm(null, list2), submit: 'Create audit', wide: true,
      onSubmit: async v => { const r = await api('POST', '/api/audits', v); location.hash = '#/audit/' + r.id; } });
  };
}

async function viewAudit(id) {
  const data = await api('GET', '/api/audits/' + id);
  const a = data.audit, p = data.perms;
  main().innerHTML = `
    <div class="crumbs"><a href="#/audits">Internal Audits</a> › <span class="mono">${esc(a.ref)}</span></div>
    <div class="pagehead"><div style="flex:1;min-width:260px"><h1>${esc(a.title)}</h1>
      <div class="sub row" style="gap:8px"><span class="mono">${esc(a.ref)}</span> ${chip(a.status, AUDIT_CLS[a.status])}</div></div>
      <div class="row">
        ${p.edit ? '<button id="editAudit">Edit details</button>' : ''}
        ${p.edit && a.status === 'Planned' ? '<button class="primary" data-st="In Progress">Start audit</button>' : ''}
        ${p.edit && a.status === 'In Progress' ? '<button class="primary" id="completeAudit">Complete audit</button>' : ''}
        ${p.reopen ? '<button data-st="In Progress">Reopen</button>' : ''}
      </div></div>
    <div class="docwrap"><div class="stack">
      <div class="card"><div class="hd"><h2>Findings</h2><span class="spacer"></span>${p.edit ? '<button class="primary small" id="addFinding">+ Record finding</button>' : ''}</div>
        <div class="bd flush">${data.findings.length ? `<div class="tablewrap"><table class="list"><thead><tr><th>Ref</th><th>Type</th><th>Finding</th><th>CAPA</th></tr></thead><tbody>
        ${data.findings.map(f => `<tr><td class="mono nowrap">${esc(f.ref)}${f.clause ? `<div class="small muted">Cl. ${esc(f.clause)}</div>` : ''}</td><td>${chip(f.type, FIND_CLS[f.type])}</td>
          <td>${esc(f.description)}${f.evidence ? `<div class="small muted" style="margin-top:4px">Evidence: ${esc(f.evidence)}</div>` : ''}</td>
          <td class="nowrap">${f.capaId ? `<a href="#/capa/${f.capaId}" class="mono">${esc(f.capaRef)}</a><div>${chip(f.capaStatus, CAPA_CLS[f.capaStatus])}</div>` : '—'}</td></tr>`).join('')}
        </tbody></table></div>` : '<div class="empty">No findings recorded.</div>'}</div></div>
      ${a.summary ? `<div class="card"><div class="hd"><h2>Audit conclusion</h2></div><div class="bd">${esc(a.summary)}</div></div>` : ''}
      <div class="card"><div class="hd"><h2>Activity</h2></div><div class="bd">${historyList(data.history)}</div></div>
    </div>
    <aside class="card meta"><div class="bd"><dl>
      <dt>Business unit</dt><dd>${esc(a.buName)}</dd><dt>Department / process</dt><dd>${esc(a.deptName)}</dd>
      <dt>Standard(s)</dt><dd>${esc(a.standard || '—')}</dd><dt>Lead auditor</dt><dd>${esc(a.leadAuditorName)}</dd>
      <dt>Planned date</dt><dd>${fmtDate(a.plannedDate)}</dd>${a.completedAt ? `<dt>Completed</dt><dd>${fmtDate(a.completedAt)}</dd>` : ''}
      <dt>Scope</dt><dd>${esc(a.scope || '—')}</dd></dl></div></aside></div>`;

  const setStatus = async (status, extra) => { try { await api('PUT', '/api/audits/' + a.id, Object.assign({ status }, extra || {})); toast('Audit ' + status.toLowerCase()); route(); } catch (e) { toast(e.message); } };
  $$('[data-st]').forEach(b => b.onclick = () => setStatus(b.dataset.st));
  if ($('#completeAudit')) $('#completeAudit').onclick = () => modal({ title: 'Complete audit', submit: 'Complete audit',
    body: field('Audit conclusion / summary', area('summary', a.summary, 5), { req: true }),
    onSubmit: async v => { await api('PUT', '/api/audits/' + a.id, { summary: v.summary, status: 'Completed' }); toast('Audit completed'); route(); } });
  if ($('#editAudit')) $('#editAudit').onclick = async () => {
    const list = await users();
    modal({ title: 'Edit audit', body: auditForm(a, list), wide: true, onSubmit: async v => { await api('PUT', '/api/audits/' + a.id, v); toast('Saved'); route(); } });
  };
  if ($('#addFinding')) $('#addFinding').onclick = async () => {
    const list = await users();
    const due = new Date(Date.now() + 30 * 864e5).toISOString().substring(0, 10);
    modal({ title: 'Record audit finding', submit: 'Record finding', wide: true,
      body: `<div class="form">
        ${field('Finding type', `<select name="type">${opts(S.settings.findingTypes.map(t => ({ value: t, label: t })), 'Minor NC')}</select>`, { req: true })}
        ${field('Clause / requirement', textIn('clause', '', 'placeholder="e.g. 8.4.1"'))}
        ${field('Finding', area('description', '', 3), { req: true, full: true })}
        ${field('Objective evidence', area('evidence', '', 2), { full: true })}
        <div class="full notice blue" id="capaBox" style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
          <div class="full"><b>Corrective action (CAPA)</b> <span class="small muted">Required for nonconformities</span>
            <label class="check small" id="raiseWrap" hidden><input type="checkbox" name="raiseCapa"> Raise a CAPA for this finding</label></div>
          ${field('Action owner', `<select name="capaOwnerId">${userOpts(list, '', 1, 'Select…')}</select>`)}
          ${field('Due date', dateIn('capaDueDate', due))}
        </div></div>`,
      onOpen: form => {
        const sync = () => { const nc = /NC$/.test($('select[name=type]', form).value); $('#raiseWrap', form).hidden = nc; };
        $('select[name=type]', form).onchange = sync; sync();
      },
      onSubmit: async v => { await api('POST', `/api/audits/${a.id}/findings`, v); toast('Finding recorded'); route(); } });
  };
}

/* ---------------- CAPA ---------------- */

async function viewCapas() {
  const q = currentQuery();
  const status = q.get('status') || 'active';
  const list = await api('GET', '/api/capas?status=' + encodeURIComponent(status));
  const filters = [['active', 'Active'], ['overdue', 'Overdue'], ['Pending Verification', 'Pending verification'], ['Closed', 'Closed'], ['all', 'All']];
  main().innerHTML = `
    <div class="pagehead"><div style="flex:1"><h1>Corrective & Preventive Actions</h1><div class="sub">${S.level >= 2 ? 'Nonconformities, root causes, actions and effectiveness verification.' : 'Corrective actions assigned to you.'}</div></div>
      ${S.level >= 2 ? '<button class="primary" id="newCapa">+ Raise CAPA</button>' : ''}</div>
    <div class="tabs">${filters.map(f => `<a href="#/capas?status=${encodeURIComponent(f[0])}" class="${status === f[0] ? 'active' : ''}">${f[1]}</a>`).join('')}</div>
    <div class="card"><div class="bd flush">${list.length ? `<div class="tablewrap"><table class="list"><thead><tr><th>Ref</th><th>Title</th><th>Source</th><th>Area</th><th>Owner</th><th>Due</th><th>Status</th></tr></thead><tbody>
      ${list.map(c => `<tr class="click" data-href="#/capa/${c.id}"><td class="mono nowrap">${esc(c.ref)}</td><td><b>${esc(c.title)}</b></td>
        <td>${esc(c.source)}${c.findingType ? `<div class="small muted">${esc(c.findingType)}</div>` : ''}</td><td>${esc(c.buName)}<div class="small muted">${esc(c.deptName)}</div></td>
        <td class="nowrap">${esc(c.ownerName)}</td><td class="nowrap">${fmtDate(c.dueDate)}</td><td>${capaChip(c)}</td></tr>`).join('')}
      </tbody></table></div>` : '<div class="empty">No corrective actions in this view.</div>'}</div></div>`;
  bindRowLinks();
  if ($('#newCapa')) $('#newCapa').onclick = async () => {
    const list2 = await users();
    const st = S.settings;
    modal({ title: 'Raise corrective action', submit: 'Raise CAPA', wide: true,
      body: `<div class="form">
        ${field('Title', textIn('title'), { req: true, full: true })}
        ${field('Source', `<select name="source">${opts(['Incident', 'Customer Complaint', 'Management Review', 'External Audit', 'Risk Assessment', 'Other'].map(x => ({ value: x, label: x })), 'Incident')}</select>`)}
        ${field('Action owner', `<select name="ownerId">${userOpts(list2, '', 1, 'Select…')}</select>`, { req: true })}
        ${field('Business unit', `<select name="bu">${codeOpts(st.businessUnits, S.me.bu, 'Select…')}</select>`, { req: true })}
        ${field('Department', `<select name="dept">${codeOpts(st.departments, S.me.dept, 'Select…')}</select>`, { req: true })}
        ${field('Due date', dateIn('dueDate', new Date(Date.now() + 30 * 864e5).toISOString().substring(0, 10)), { req: true })}
        ${field('Description of the problem', area('description', '', 4), { full: true })}
      </div>`,
      onSubmit: async v => { const r = await api('POST', '/api/capas', v); location.hash = '#/capa/' + r.id; } });
  };
}

async function viewCapa(id) {
  const data = await api('GET', '/api/capas/' + id);
  const c = data.capa, p = data.perms;
  const stages = ['Open', 'In Progress', 'Pending Verification', 'Closed'];
  const idx = stages.indexOf(c.status);
  const ro = (label, v) => `<div><div class="small muted" style="text-transform:uppercase;letter-spacing:.05em;font-size:11.5px">${esc(label)}</div><div style="white-space:pre-wrap;margin-top:3px">${v ? esc(v) : '<span class="muted">Not yet recorded</span>'}</div></div>`;
  main().innerHTML = `
    <div class="crumbs"><a href="#/capas">CAPA</a> › <span class="mono">${esc(c.ref)}</span></div>
    <div class="pagehead"><div style="flex:1;min-width:260px"><h1>${esc(c.title)}</h1><div class="sub row" style="gap:8px"><span class="mono">${esc(c.ref)}</span> ${capaChip(c)}</div></div>
      <div class="row">${p.editHeader ? '<button id="editHead">Edit / reassign</button>' : ''}</div></div>
    <div class="card" style="margin-bottom:16px"><div class="bd"><div class="steps">${stages.map((s, i) => `<div class="${i < idx || c.status === 'Closed' ? 'done' : ''} ${i === idx && c.status !== 'Closed' ? 'cur' : ''}">${s}</div>`).join('')}</div></div></div>
    ${c.verificationComment && c.status !== 'Closed' ? `<div class="notice red" style="margin-bottom:16px"><b>Verification: not yet effective.</b> ${esc(c.verificationComment)} <span class="small muted">— ${esc(c.verifiedByName)}</span></div>` : ''}
    ${p.verify ? `<div class="notice blue" style="margin-bottom:16px"><b>Verify the effectiveness of this action.</b><span class="spacer"></span>
      <button class="success small" id="vOk">Effective — close CAPA</button><button class="danger small" id="vNo">Not effective</button></div>` : ''}
    <div class="docwrap"><div class="stack">
      <div class="card"><div class="hd"><h2>Problem</h2></div><div class="bd">${esc(c.description || '—')}
        ${c.findingRef ? `<div class="small muted" style="margin-top:8px">From audit finding <a href="#/audit/${c.auditId}">${esc(c.findingRef)}</a> (${esc(c.findingType)})</div>` : ''}</div></div>
      <form class="card" id="capaForm"><div class="hd"><h2>Investigation and action</h2></div><div class="bd stack">
        <div class="error" hidden></div>
        ${p.edit ? `${field('Root cause', area('rootCause', c.rootCause, 3), { hint: 'Why did it happen? (e.g. 5 Whys, fishbone)' })}
          ${field('Immediate correction', area('correction', c.correction, 2), { hint: 'What was done to contain or fix the problem?' })}
          ${field('Corrective action', area('correctiveAction', c.correctiveAction, 3), { hint: 'What was changed to stop it happening again?' })}
          ${field('Evidence of implementation', area('evidence', c.evidence, 2))}
          <div class="row"><button class="primary">Save</button>${p.submit ? '<button type="button" id="submitCapa">Submit for verification</button>' : ''}</div>`
        : ro('Root cause', c.rootCause) + ro('Immediate correction', c.correction) + ro('Corrective action', c.correctiveAction) + ro('Evidence of implementation', c.evidence)}
        ${c.status === 'Closed' ? `<div class="notice green"><b>Verified effective</b> by ${esc(c.verifiedByName)} on ${fmtDate(c.verifiedAt)}${c.verificationComment ? ' — ' + esc(c.verificationComment) : ''}</div>` : ''}
      </div></form>
      <div class="card"><div class="hd"><h2>Activity</h2></div><div class="bd">${historyList(data.history)}</div></div>
    </div>
    <aside class="card meta"><div class="bd"><dl>
      <dt>Source</dt><dd>${esc(c.source)}</dd><dt>Action owner</dt><dd>${esc(c.ownerName)}</dd>
      <dt>Due date</dt><dd>${fmtDate(c.dueDate)} ${c.overdue ? chip('Overdue', 'red') : ''}</dd>
      <dt>Business unit</dt><dd>${esc(c.buName)}</dd><dt>Department</dt><dd>${esc(c.deptName)}</dd>
      <dt>Raised by</dt><dd>${esc(c.createdByName)}<div class="small muted">${fmtDate(c.createdAt)}</div></dd>
      ${c.closedAt ? `<dt>Closed</dt><dd>${fmtDate(c.closedAt)}</dd>` : ''}</dl></div></aside></div>`;

  const f = $('#capaForm');
  if (p.edit) {
    f.onsubmit = async e => { e.preventDefault(); try { await api('PUT', '/api/capas/' + c.id, formData(f)); toast('Saved'); route(); } catch (ex) { $('.error', f).textContent = ex.message; $('.error', f).hidden = false; } };
    if ($('#submitCapa')) $('#submitCapa').onclick = async () => {
      try { await api('PUT', '/api/capas/' + c.id, formData(f)); await api('POST', `/api/capas/${c.id}/submit`); toast('Submitted for verification'); route(); }
      catch (ex) { $('.error', f).textContent = ex.message; $('.error', f).hidden = false; }
    };
  }
  if ($('#vOk')) $('#vOk').onclick = () => modal({ title: 'Verify and close CAPA', submit: 'Close CAPA',
    body: field('Verification notes', area('comment', '', 3), { hint: 'How was effectiveness confirmed?' }),
    onSubmit: async v => { await api('POST', `/api/capas/${c.id}/verify`, { effective: true, comment: v.comment }); toast('CAPA closed'); route(); } });
  if ($('#vNo')) $('#vNo').onclick = () => modal({ title: 'Action not effective', submit: 'Return to owner', danger: true,
    body: field('Why is the action not yet effective?', area('comment', '', 3), { req: true }),
    onSubmit: async v => { await api('POST', `/api/capas/${c.id}/verify`, { effective: false, comment: v.comment }); toast('Returned to owner'); route(); } });
  if ($('#editHead')) $('#editHead').onclick = async () => {
    const list = await users();
    modal({ title: 'Edit CAPA', wide: true, body: `<div class="form">${field('Title', textIn('title', c.title), { req: true, full: true })}
      ${field('Action owner', `<select name="ownerId">${userOpts(list, c.ownerId, 1)}</select>`)}${field('Due date', dateIn('dueDate', c.dueDate))}
      ${field('Description', area('description', c.description, 4), { full: true })}</div>`,
      onSubmit: async v => { await api('PUT', '/api/capas/' + c.id, v); toast('Saved'); route(); } });
  };
}

/* ---------------- reports ---------------- */

async function viewReports() {
  const r = await api('GET', '/api/reports');
  const k = r.kpi;
  const hbars = list => { const max = Math.max(1, ...list.map(x => x.count)); return list.map(x => `<div class="hbar"><span>${esc(x.name)}</span><div class="track"><i style="width:${100 * x.count / max}%"></i></div><b class="right">${x.count}</b></div>`).join(''); };
  main().innerHTML = `
    <div class="pagehead"><div style="flex:1"><h1>IMS Reports</h1><div class="sub">Status of controlled documents, acknowledgements, audits and corrective actions.</div></div><button onclick="window.print()">Print</button></div>
    <div class="kpis">
      <div class="kpi"><div class="l">Effective documents</div><div class="v">${k.effective}</div></div>
      <div class="kpi"><div class="l">Revisions in approval</div><div class="v">${k.inApproval}</div></div>
      <div class="kpi"><div class="l">Drafts</div><div class="v">${k.drafts}</div></div>
      <div class="kpi ${k.reviewsOverdue ? 'warn' : ''}"><div class="l">Reviews overdue</div><div class="v">${k.reviewsOverdue}</div></div>
      <div class="kpi"><div class="l">Acknowledgement rate</div><div class="v">${k.ackRate}%</div></div>
      <div class="kpi"><div class="l">Open CAPA</div><div class="v">${k.capaOpen}</div></div>
      <div class="kpi ${k.capaOverdue ? 'warn' : ''}"><div class="l">Overdue CAPA</div><div class="v">${k.capaOverdue}</div></div>
      <div class="kpi"><div class="l">Open audits</div><div class="v">${k.auditsOpen}</div></div>
    </div>
    <div class="grid3" style="margin-bottom:16px">
      <div class="card"><div class="hd"><h2>Effective documents by type</h2></div><div class="bd">${hbars(r.byType)}</div></div>
      <div class="card"><div class="hd"><h2>By company / BU</h2></div><div class="bd">${hbars(r.byBu)}</div></div>
      <div class="card"><div class="hd"><h2>CAPA by status</h2></div><div class="bd">${hbars(r.capaByStatus)}<div style="margin-top:16px"></div><h3 style="margin-bottom:6px">Audit findings by type</h3>${hbars(r.findingsByType)}</div></div>
    </div>
    <div class="grid2">
      <div class="card"><div class="hd"><h2>Acknowledgement compliance</h2></div><div class="bd flush tablewrap"><table class="list"><thead><tr><th>Document</th><th>Rev</th><th>Acknowledged</th><th style="width:150px"></th></tr></thead><tbody>
        ${r.acks.map(a => `<tr class="click" data-href="#/doc/${a.id}?tab=acks"><td><b>${esc(a.title)}</b><div class="small muted mono">${esc(a.docNo)}</div></td><td>${esc(a.rev)}</td>
          <td class="nowrap">${a.acknowledged} / ${a.required} (${a.pct}%)</td><td><div class="bar ${a.pct < 50 ? 'low' : a.pct < 80 ? 'mid' : ''}"><i style="width:${a.pct}%"></i></div></td></tr>`).join('')}
      </tbody></table></div></div>
      <div class="stack">
        <div class="card"><div class="hd"><h2>Periodic reviews due</h2></div><div class="bd flush">${r.reviews.length ? `<table class="list"><tbody>${r.reviews.map(d => `<tr class="click" data-href="#/doc/${d.id}">
          <td><b>${esc(d.title)}</b><div class="small muted"><span class="mono">${esc(d.docNo)}</span> · ${esc(d.ownerName)}</div></td><td class="nowrap">${fmtDate(d.nextReviewDate)}<div>${reviewChip(d)}</div></td></tr>`).join('')}</tbody></table>`
          : '<div class="empty">No reviews due in the next 30 days.</div>'}</div></div>
        <div class="card"><div class="hd"><h2>Overdue corrective actions</h2></div><div class="bd flush">${r.capaOverdue.length ? `<table class="list"><tbody>${r.capaOverdue.map(c => `<tr class="click" data-href="#/capa/${c.id}">
          <td><b>${esc(c.title)}</b><div class="small muted"><span class="mono">${esc(c.ref)}</span> · ${esc(c.ownerName)}</div></td><td class="nowrap">${fmtDate(c.dueDate)}</td></tr>`).join('')}</tbody></table>`
          : '<div class="empty">No overdue corrective actions.</div>'}</div></div>
      </div>
    </div>`;
  bindRowLinks();
}

/* ---------------- users ---------------- */

async function viewUsers() {
  S.users = null;
  const list = await users();
  const st = S.settings;
  const ROLE_HELP = { Employee: 'Read, search and acknowledge', Contributor: 'Process owner / approver / auditor', Controller: 'Document controller — publishes', Admin: 'Full administration' };
  main().innerHTML = `
    <div class="pagehead"><div style="flex:1"><h1>Users & Access</h1><div class="sub">Access is granted by company/BU, department and role.</div></div><button class="primary" id="addUser">+ Add user</button></div>
    <div class="card"><div class="bd flush tablewrap"><table class="list"><thead><tr><th>Name</th><th>Username</th><th>Role</th><th>Company / BU</th><th>Department</th><th>Status</th><th></th></tr></thead><tbody>
      ${list.map(u => `<tr><td><b>${esc(u.name)}</b><div class="small muted">${esc(u.position || '')}</div></td><td class="mono">${esc(u.username)}</td>
        <td>${esc(u.role)}<div class="small muted">${esc(ROLE_HELP[u.role] || '')}</div></td><td>${esc(u.buName)}</td><td>${esc(u.deptName)}</td>
        <td>${u.active ? chip('Active', 'green') : chip('Inactive')} ${u.locked ? chip('Locked', 'red') : ''} ${u.mustChangePassword ? chip('Must change password', 'amber') : ''}</td>
        <td class="nowrap right"><button class="small" data-edit="${u.id}">Edit</button> <button class="small" data-reset="${u.id}">Reset password</button></td></tr>`).join('')}
    </tbody></table></div></div>`;
  const form = u => `<div class="form">
    ${field('Full name', textIn('name', u.name), { req: true })}
    ${field('Username', textIn('username', u.username, u.id ? 'disabled' : 'autocomplete="off"'), { req: true })}
    ${field('Position', textIn('position', u.position))}
    ${field('Email', `<input type="email" name="email" value="${esc(u.email || '')}">`)}
    ${field('Role', `<select name="role">${opts(st.roles.map(r => ({ value: r, label: r + ' — ' + ROLE_HELP[r] })), u.role || 'Employee')}</select>`, { req: true, full: true })}
    ${field('Company / BU', `<select name="bu">${codeOpts(st.businessUnits.filter(b => b.Code !== 'GRP'), u.bu, 'Select…')}</select>`, { req: true })}
    ${field('Department', `<select name="dept">${codeOpts(st.departments, u.dept, 'Select…')}</select>`, { req: true })}
    ${u.id ? `<label class="check full"><input type="checkbox" name="active" ${u.active ? 'checked' : ''}> Account active</label>
      ${u.locked ? '<label class="check full"><input type="checkbox" name="unlock"> Unlock account</label>' : ''}`
      : field('Temporary password', '<input type="password" name="password" autocomplete="new-password">', { req: true, full: true, hint: 'At least 8 characters with letters and numbers. The user must change it at first sign-in.' })}
  </div>`;
  $('#addUser').onclick = () => modal({ title: 'Add user', body: form({}), wide: true, submit: 'Create user',
    onSubmit: async v => { await api('POST', '/api/users', v); toast('User created'); route(); } });
  $$('[data-edit]').forEach(b => b.onclick = () => { const u = list.find(x => x.id == b.dataset.edit);
    modal({ title: 'Edit user', body: form(u), wide: true, onSubmit: async v => { await api('PUT', '/api/users/' + u.id, v); toast('User updated'); route(); } }); });
  $$('[data-reset]').forEach(b => b.onclick = () => { const u = list.find(x => x.id == b.dataset.reset);
    modal({ title: 'Reset password — ' + u.name, submit: 'Reset password',
      body: field('New temporary password', '<input type="password" name="password" autocomplete="new-password">', { req: true, hint: 'The user must change it at next sign-in.' }),
      onSubmit: async v => { await api('POST', `/api/users/${u.id}/reset-password`, v); toast('Password reset'); route(); } }); });
}

/* ---------------- audit trail ---------------- */

async function viewLog() {
  const q = currentQuery();
  const params = new URLSearchParams();
  if (q.get('q')) params.set('q', q.get('q'));
  if (q.get('entity')) params.set('entity', q.get('entity'));
  const list = await api('GET', '/api/log?' + params);
  const link = l => l.entity === 'Document' ? `#/doc/${l.entityId}?tab=activity` : l.entity === 'Audit' ? `#/audit/${l.entityId}` : l.entity === 'CAPA' ? `#/capa/${l.entityId}` : null;
  main().innerHTML = `
    <div class="pagehead"><div style="flex:1"><h1>Audit Trail</h1><div class="sub">Record of sign-ins, document changes, approvals, publications, acknowledgements and CAPA activity. Entries cannot be edited.</div></div></div>
    <form class="card" id="lf" style="margin-bottom:16px"><div class="bd row">
      <input type="search" name="q" value="${esc(q.get('q') || '')}" placeholder="Search user, action or detail" style="flex:1;min-width:220px">
      <select name="entity" style="width:180px">${opts(['Document', 'Audit', 'CAPA', 'User', 'System'].map(x => ({ value: x, label: x })), q.get('entity'), 'All records')}</select>
      <button class="primary">Filter</button></div></form>
    <div class="card"><div class="bd flush tablewrap"><table class="list"><thead><tr><th>When</th><th>User</th><th>Action</th><th>Record</th><th>Detail</th></tr></thead><tbody>
      ${list.map(l => `<tr><td class="nowrap small">${fmtDate(l.at)}</td><td class="nowrap">${esc(l.userName)}</td><td>${esc(l.action)}</td>
        <td class="nowrap">${link(l) ? `<a href="${link(l)}" class="mono">${esc(l.label || l.entity + ' ' + l.entityId)}</a>` : esc(l.entity)}</td><td class="small">${esc(l.detail)}</td></tr>`).join('')
      || '<tr><td colspan="5" class="empty">No entries.</td></tr>'}</tbody></table></div></div>`;
  const f = $('#lf');
  f.onsubmit = e => { e.preventDefault(); const d = formData(f), p = new URLSearchParams(); if (d.q) p.set('q', d.q); if (d.entity) p.set('entity', d.entity); location.hash = '#/log?' + p; };
}

/* ---------------- profile ---------------- */

async function viewProfile() {
  const u = S.me;
  main().innerHTML = `
    <div class="pagehead"><div><h1>My Profile</h1></div></div>
    ${u.mustChangePassword ? '<div class="notice amber" style="margin-bottom:16px"><b>Please set a new password before continuing.</b></div>' : ''}
    <div class="grid2">
      <div class="card meta"><div class="hd"><h2>Account</h2></div><div class="bd"><dl>
        <dt>Name</dt><dd>${esc(u.name)}</dd><dt>Username</dt><dd class="mono">${esc(u.username)}</dd><dt>Position</dt><dd>${esc(u.position || '—')}</dd>
        <dt>Role</dt><dd>${esc(u.role)}</dd><dt>Company / BU</dt><dd>${esc(u.buName)}</dd><dt>Department</dt><dd>${esc(u.deptName)}</dd></dl>
        <p class="small muted">Contact the IMS administrator to change your role, company or department.</p></div></div>
      <form class="card" id="pw"><div class="hd"><h2>Change password</h2></div><div class="bd stack">
        <div class="error" hidden></div>
        ${field('Current password', '<input type="password" name="current" autocomplete="current-password">')}
        ${field('New password', '<input type="password" name="password" autocomplete="new-password">', { hint: 'At least 8 characters with letters and numbers.' })}
        ${field('Confirm new password', '<input type="password" name="confirm" autocomplete="new-password">')}
        <button class="primary">Change password</button></div></form>
    </div>`;
  const f = $('#pw');
  f.onsubmit = async e => {
    e.preventDefault();
    const v = formData(f), err = $('.error', f);
    err.hidden = true;
    try {
      if (v.password !== v.confirm) throw new Error('The new passwords do not match.');
      await api('POST', '/api/me/password', { current: v.current, password: v.password });
      toast('Password changed');
      S.me.mustChangePassword = false;
      f.reset();
      if (location.hash === '#/profile') route();
    } catch (ex) { err.textContent = ex.message; err.hidden = false; }
  };
}

boot();
