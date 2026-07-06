/* ================= RWVDCS.Next 管理台 ================= */
"use strict";

// ------------------------- 基础设施 -------------------------
const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

async function api(path, opts = {}) {
  const init = { headers: {} };
  if (opts.method) init.method = opts.method;
  if (opts.body !== undefined) {
    init.headers["Content-Type"] = "application/json";
    init.body = JSON.stringify(opts.body);
  }
  const resp = await fetch("/api" + path, init);
  const text = await resp.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  if (!resp.ok) throw new Error(data && data.error ? data.error : `HTTP ${resp.status}`);
  return data;
}

function toast(msg, kind = "info", ms = 3800) {
  const el = document.createElement("div");
  el.className = "toast " + (kind === "error" ? "err" : kind === "ok" ? "ok" : "");
  el.textContent = msg;
  $("#toast-box").appendChild(el);
  setTimeout(() => el.remove(), ms);
}

function modal(title, bodyHtml, onMount) {
  const root = $("#modal-root");
  const mask = document.createElement("div");
  mask.className = "modal-mask";
  mask.innerHTML = `<div class="modal">
    <div class="m-head"><span>${esc(title)}</span><span class="m-close">✕</span></div>
    <div class="m-body">${bodyHtml}</div>
  </div>`;
  const close = () => mask.remove();
  mask.addEventListener("click", e => { if (e.target === mask) close(); });
  $(".m-close", mask).addEventListener("click", close);
  root.appendChild(mask);
  if (onMount) onMount(mask, close);
  return close;
}

function fmtVal(v) {
  if (v === null || v === undefined) return "<null>";
  if (typeof v === "number") {
    if (Number.isInteger(v)) return String(v);
    return Number(v.toPrecision(7)).toString();
  }
  if (typeof v === "boolean") return v ? "1" : "0";
  if (typeof v === "object" && v.length !== undefined && v.preview) return `[${v.length}] ${v.preview.slice(0, 6).join(", ")}…`;
  return String(v);
}

function fmtBytes(n) {
  if (n > 1024 * 1024 * 1024) return (n / 1024 / 1024 / 1024).toFixed(2) + " GB";
  if (n > 1024 * 1024) return (n / 1024 / 1024).toFixed(1) + " MB";
  if (n > 1024) return (n / 1024).toFixed(1) + " KB";
  return n + " B";
}

function fmtTime(iso) {
  const d = new Date(iso);
  return isNaN(d) ? "-" : d.toLocaleString("zh-CN", { hour12: false });
}

// 收藏（localStorage）
const favStore = {
  get points() { return JSON.parse(localStorage.getItem("fav:points") || "[]"); },
  set points(v) { localStorage.setItem("fav:points", JSON.stringify(v)); },
  get blocks() { return JSON.parse(localStorage.getItem("fav:blocks") || "[]"); },
  set blocks(v) { localStorage.setItem("fav:blocks", JSON.stringify(v)); },
  togglePoint(name) {
    const list = favStore.points;
    const i = list.indexOf(name);
    if (i >= 0) list.splice(i, 1); else list.push(name);
    favStore.points = list;
    return i < 0;
  },
  toggleBlock(dpu, name) {
    const list = favStore.blocks;
    const i = list.findIndex(b => b.dpu === dpu && b.name === name);
    if (i >= 0) list.splice(i, 1); else list.push({ dpu, name });
    favStore.blocks = list;
    return i < 0;
  },
  hasPoint(name) { return favStore.points.includes(name); },
  hasBlock(dpu, name) { return favStore.blocks.some(b => b.dpu === dpu && b.name === name); },
};

// ------------------------- 顶栏运行控制 -------------------------
async function refreshTopbar() {
  try {
    const st = await api("/status");
    const p = st.project;
    $("#project-info").textContent = p
      ? `${p.mdbPath.split(/[\\/]/).pop()} ｜ ${p.dpuCount} DPU / ${p.pointCount.toLocaleString()} 点 / ${p.commandCount.toLocaleString()} 块 ｜ 指纹 ${p.fingerprint} ｜ v${p.version}`
      : "未装载工程";
    const state = st.run.state;
    const badge = $("#run-state");
    badge.textContent = state === "Running" ? "运行中" : state === "Paused" ? "已暂停" : "已停止";
    badge.className = "badge " + (state === "Running" ? "state-running" : state === "Paused" ? "state-paused" : "state-stopped");
    window.__status = st;
  } catch { /* 服务未就绪 */ }
}

function bindTopbar() {
  $("#btn-run").onclick = () => api("/run/start", { method: "POST" }).then(refreshTopbar).catch(e => toast(e.message, "error"));
  $("#btn-pause").onclick = () => api("/run/pause", { method: "POST" }).then(refreshTopbar).catch(e => toast(e.message, "error"));
  $("#btn-stop").onclick = () => api("/run/stop", { method: "POST" }).then(refreshTopbar).catch(e => toast(e.message, "error"));
  $("#btn-step").onclick = () => {
    const cycles = parseInt($("#step-cycles").value) || 1;
    api("/run/step", { method: "POST", body: { cycles } })
      .then(() => { refreshTopbar(); toast(`单步 ${cycles} 周期完成`, "ok", 1600); if (window.__viewRefresh) window.__viewRefresh(); })
      .catch(e => toast(e.message, "error"));
  };
}

// ------------------------- 路由 -------------------------
const views = {};
let currentTimer = null;

function navigate() {
  const hash = location.hash || "#/dashboard";
  const [, path, queryStr] = hash.match(/^#\/([^?]*)\??(.*)$/) || [];
  const query = Object.fromEntries(new URLSearchParams(queryStr || ""));
  const name = (path || "dashboard").split("/")[0];
  const view = views[name] || views.dashboard;

  if (currentTimer) { clearInterval(currentTimer); currentTimer = null; }
  window.__viewRefresh = null;
  $("#modal-root").innerHTML = "";
  $$("#sidebar a").forEach(a => a.classList.toggle("active", a.dataset.view === name));
  const main = $("#main");
  main.innerHTML = "";
  view(main, query);
}

function setViewTimer(fn, ms) {
  if (currentTimer) clearInterval(currentTimer);
  currentTimer = setInterval(fn, ms);
}

// ------------------------- 视图：总览 -------------------------
views.dashboard = (main) => {
  main.innerHTML = `
    <h2>系统总览</h2>
    <div class="toolbar">
      <label>工程库</label>
      <input id="proj-mdb" style="width:420px" placeholder="D:\\path\\to\\project.mdb">
      <button id="proj-load" class="btn btn-primary">装载工程</button>
      <span class="dim">（装载会重建整套运行时；已有工程时相当于冷替换）</span>
    </div>
    <div class="cards" id="dash-cards"></div>
    <div class="toolbar">
      <h3 style="margin:0">DPU 列表</h3>
      <span class="spacer"></span>
      <label class="dim">统一周期(秒)</label>
      <input id="uni-cycle" type="number" step="0.05" min="0.01" style="width:90px" placeholder="0.2">
      <button id="btn-uni-cycle" class="btn btn-mini">统一设置</button>
    </div>
    <table class="grid" id="dpu-table"><thead><tr>
      <th>DPU</th><th class="num">Id</th><th>状态</th><th class="num">周期 (s)</th><th></th>
      <th class="num">周期数</th><th class="num">avg ms</th><th class="num">max ms</th><th class="num">p99 ms</th><th class="num">超限</th>
    </tr></thead><tbody></tbody></table>
    <p class="mini-note">周期修改在周期边界生效；单个 DPU 修改后按回车或点“设”。</p>`;

  const st0 = window.__status;
  if (st0 && st0.project) $("#proj-mdb").value = st0.project.mdbPath;
  $("#proj-load").onclick = async () => {
    const mdbPath = $("#proj-mdb").value.trim();
    if (!mdbPath) return toast("请输入工程库路径", "error");
    if (window.__status && window.__status.project && !confirm("已有工程在运行，装载将整体替换（在线保留状态请走“在线下装”）。继续？")) return;
    $("#proj-load").disabled = true;
    try {
      const r = await api("/project/load", { method: "POST", body: { mdbPath } });
      toast(`工程已装载（指纹 ${r.fingerprint}，v${r.version}）`, "ok");
      await refreshTopbar();
      refresh();
    } catch (e) { toast(e.message, "error"); }
    $("#proj-load").disabled = false;
  };

  $("#btn-uni-cycle").onclick = () => {
    const sec = parseFloat($("#uni-cycle").value);
    if (!sec) return toast("请输入周期秒数", "error");
    api("/dpus/cycle", { method: "PUT", body: { seconds: sec } })
      .then(() => { toast("已统一设置周期", "ok"); refresh(); })
      .catch(e => toast(e.message, "error"));
  };

  async function refresh() {
    try {
      const st = window.__status || await api("/status");
      const cards = $("#dash-cards");
      const p = st.project, m = st.monitor;
      cards.innerHTML = `
        <div class="card"><div class="k">工程</div><div class="v">${p ? esc(p.mdbPath.split(/[\\/]/).pop()) : "-"}<br><small>${p ? `指纹 ${p.fingerprint} ｜ v${p.version}` : ""}</small></div></div>
        <div class="card"><div class="k">规模</div><div class="v">${p ? `${p.dpuCount} DPU` : "-"}<br><small>${p ? `${p.pointCount.toLocaleString()} 点 + ${p.intermediatePointCount.toLocaleString()} 中间点 / ${p.commandCount.toLocaleString()} 块` : ""}</small></div></div>
        <div class="card"><div class="k">托管堆 / 工作集</div><div class="v">${m.heapMb.toFixed(0)} / ${m.workingSetMb.toFixed(0)} MB<br><small>GC ${m.gen0}/${m.gen1}/${m.gen2} ｜ 暂停 ${m.gcPausePct.toFixed(2)}%</small></div></div>
        <div class="card"><div class="k">线程 / 历史站</div><div class="v">${m.threads}<small> 线程</small> ｜ ${m.historyMb.toFixed(1)}<small> MB</small></div></div>`;

      const dpus = await api("/dpus");
      const tb = $("#dpu-table tbody");
      tb.innerHTML = dpus.map(d => `<tr>
        <td class="mono">${esc(d.name)}</td>
        <td class="num">${d.controllerId}</td>
        <td><span class="badge ${d.state === "Running" ? "state-running" : d.state === "Paused" ? "state-paused" : "state-stopped"}">${d.state === "Running" ? "运行" : d.state === "Paused" ? "暂停" : "停止"}</span></td>
        <td class="num"><input class="cell-edit dpu-cycle" data-dpu="${esc(d.name)}" type="number" step="0.05" min="0.01" value="${d.cycleSeconds.toFixed(2)}" style="width:76px"></td>
        <td><button class="btn btn-mini set-cycle" data-dpu="${esc(d.name)}">设</button></td>
        <td class="num">${d.cycleCount.toLocaleString()}</td>
        <td class="num">${d.stats ? d.stats.avgMs.toFixed(2) : "-"}</td>
        <td class="num">${d.stats ? d.stats.maxMs.toFixed(1) : "-"}</td>
        <td class="num">${d.stats ? d.stats.p99Ms.toFixed(1) : "-"}</td>
        <td class="num">${d.stats ? d.stats.overruns : "-"}</td>
      </tr>`).join("");

      $$(".set-cycle", tb).forEach(btn => btn.onclick = () => setOne(btn.dataset.dpu));
      $$(".dpu-cycle", tb).forEach(inp => inp.onkeydown = e => { if (e.key === "Enter") setOne(inp.dataset.dpu); });
      function setOne(dpu) {
        const inp = $(`.dpu-cycle[data-dpu="${CSS.escape(dpu)}"]`, tb);
        const sec = parseFloat(inp.value);
        if (!sec) return;
        api(`/dpus/${encodeURIComponent(dpu)}/cycle`, { method: "PUT", body: { seconds: sec } })
          .then(() => toast(`${dpu} 周期 → ${sec}s`, "ok", 1500))
          .catch(e => toast(e.message, "error"));
      }
    } catch (e) { /* 忽略瞬时错误 */ }
  }

  refresh();
  window.__viewRefresh = refresh;
  setViewTimer(async () => { await refreshTopbar(); refresh(); }, 1500);
};

// ------------------------- 视图：点与功能块 -------------------------
views.browse = (main, query) => {
  const mode = query.tab || "blocks";
  main.innerHTML = `
    <h2>点与功能块</h2>
    <div class="tabs">
      <div class="tab ${mode === "blocks" ? "active" : ""}" data-tab="blocks">功能块</div>
      <div class="tab ${mode === "points" ? "active" : ""}" data-tab="points">点</div>
      <div class="tab ${mode === "fav" ? "active" : ""}" data-tab="fav">收藏夹</div>
    </div>
    <div id="browse-body"></div>`;
  $$(".tab", main).forEach(t => t.onclick = () => { location.hash = `#/browse?tab=${t.dataset.tab}`; });

  const body = $("#browse-body");
  if (mode === "blocks") renderBlocks(body);
  else if (mode === "points") renderPoints(body);
  else renderFavorites(body);
};

async function loadDpuOptions(sel) {
  try {
    const dpus = await api("/dpus");
    sel.innerHTML = `<option value="">全部 DPU</option>` +
      dpus.map(d => `<option value="${esc(d.name)}">${esc(d.name)}</option>`).join("");
  } catch { }
}

function renderBlocks(body) {
  body.innerHTML = `
    <div class="toolbar">
      <input id="q" placeholder="按块名搜索…" style="width:220px">
      <select id="fc-filter"><option value="">全部功能码</option></select>
      <select id="dpu-filter"></select>
      <button id="btn-search" class="btn btn-primary">搜索</button>
      <span class="spacer"></span><span id="total" class="dim"></span>
    </div>
    <table class="grid"><thead><tr>
      <th style="width:30px"></th><th>块名</th><th>功能码</th><th>DPU</th><th class="num">入/出</th><th>强制</th><th></th>
    </tr></thead><tbody id="rows"></tbody></table>
    <div class="pager">
      <button id="prev" class="btn btn-mini">上一页</button>
      <span id="pageinfo"></span>
      <button id="next" class="btn btn-mini">下一页</button>
    </div>`;

  api("/fcs").then(fcs => {
    $("#fc-filter").innerHTML = `<option value="">全部功能码</option>` +
      fcs.map(f => `<option value="${esc(f.fc)}">${esc(f.fc)}（${f.count}）</option>`).join("");
  }).catch(() => { });
  loadDpuOptions($("#dpu-filter"));

  let page = 1;
  async function load() {
    const params = new URLSearchParams({ page, pageSize: 50 });
    if ($("#q").value) params.set("q", $("#q").value);
    if ($("#fc-filter").value) params.set("fc", $("#fc-filter").value);
    if ($("#dpu-filter").value) params.set("dpu", $("#dpu-filter").value);
    const data = await api("/blocks?" + params);
    $("#total").textContent = `共 ${data.total.toLocaleString()} 块`;
    $("#pageinfo").textContent = `第 ${data.page} / ${Math.max(1, Math.ceil(data.total / data.pageSize))} 页`;
    $("#rows").innerHTML = data.items.map(b => `<tr>
      <td><span class="fav ${favStore.hasBlock(b.dpu, b.name) ? "on" : ""}" data-dpu="${esc(b.dpu)}" data-name="${esc(b.name)}">★</span></td>
      <td class="mono"><a class="link" href="#/pointinfo?type=block&dpu=${encodeURIComponent(b.dpu)}&name=${encodeURIComponent(b.name)}">${esc(b.name)}</a></td>
      <td>${esc(b.fc)}</td><td>${esc(b.dpu)}</td>
      <td class="num">${b.inputs}/${b.outputs}</td>
      <td>${b.forced ? '<span class="badge badge-warn">强制</span>' : ""}</td>
      <td><a class="link" href="#/pointinfo?type=block&dpu=${encodeURIComponent(b.dpu)}&name=${encodeURIComponent(b.name)}">PointInfo →</a></td>
    </tr>`).join("");
    $$(".fav", $("#rows")).forEach(f => f.onclick = () => {
      const on = favStore.toggleBlock(f.dataset.dpu, f.dataset.name);
      f.classList.toggle("on", on);
    });
  }

  $("#btn-search").onclick = () => { page = 1; load().catch(e => toast(e.message, "error")); };
  $("#q").onkeydown = e => { if (e.key === "Enter") $("#btn-search").click(); };
  $("#prev").onclick = () => { if (page > 1) { page--; load(); } };
  $("#next").onclick = () => { page++; load().catch(() => page--); };
  load().catch(e => toast(e.message, "error"));
}

function renderPoints(body) {
  body.innerHTML = `
    <div class="toolbar">
      <input id="q" placeholder="按点名搜索…" style="width:220px">
      <select id="kind-filter">
        <option value="">全部类型</option>
        <option>LA</option><option>LD</option><option>LP</option><option>LP32</option>
      </select>
      <select id="dpu-filter"></select>
      <button id="btn-search" class="btn btn-primary">搜索</button>
      <span class="spacer"></span><span id="total" class="dim"></span>
    </div>
    <table class="grid"><thead><tr>
      <th style="width:30px"></th><th>点名</th><th>类型</th><th>DPU</th><th class="num">当前值</th><th>强制</th><th></th>
    </tr></thead><tbody id="rows"></tbody></table>
    <div class="pager">
      <button id="prev" class="btn btn-mini">上一页</button>
      <span id="pageinfo"></span>
      <button id="next" class="btn btn-mini">下一页</button>
    </div>`;

  loadDpuOptions($("#dpu-filter"));
  let page = 1;
  async function load() {
    const params = new URLSearchParams({ page, pageSize: 50 });
    if ($("#q").value) params.set("q", $("#q").value);
    if ($("#kind-filter").value) params.set("kind", $("#kind-filter").value);
    if ($("#dpu-filter").value) params.set("dpu", $("#dpu-filter").value);
    const data = await api("/points?" + params);
    $("#total").textContent = `共 ${data.total.toLocaleString()} 点`;
    $("#pageinfo").textContent = `第 ${data.page} / ${Math.max(1, Math.ceil(data.total / data.pageSize))} 页`;
    $("#rows").innerHTML = data.items.map(p => `<tr>
      <td><span class="fav ${favStore.hasPoint(p.name) ? "on" : ""}" data-name="${esc(p.name)}">★</span></td>
      <td class="mono"><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(p.name)}">${esc(p.name)}</a></td>
      <td>${p.kind}</td><td>${esc(p.dpu)}</td>
      <td class="num">${fmtVal(p.value)}</td>
      <td>${p.forced ? '<span class="badge badge-warn">强制</span>' : ""}</td>
      <td><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(p.name)}">PointInfo →</a></td>
    </tr>`).join("");
    $$(".fav", $("#rows")).forEach(f => f.onclick = () => {
      const on = favStore.togglePoint(f.dataset.name);
      f.classList.toggle("on", on);
    });
  }

  $("#btn-search").onclick = () => { page = 1; load().catch(e => toast(e.message, "error")); };
  $("#q").onkeydown = e => { if (e.key === "Enter") $("#btn-search").click(); };
  $("#prev").onclick = () => { if (page > 1) { page--; load(); } };
  $("#next").onclick = () => { page++; load().catch(() => page--); };
  load().catch(e => toast(e.message, "error"));
}

function renderFavorites(body) {
  const blocks = favStore.blocks, points = favStore.points;
  body.innerHTML = `
    <h3>收藏的功能块（${blocks.length}）</h3>
    <table class="grid"><tbody id="fav-blocks">${blocks.map(b => `<tr>
      <td style="width:30px"><span class="fav on" data-kind="block" data-dpu="${esc(b.dpu)}" data-name="${esc(b.name)}">★</span></td>
      <td class="mono"><a class="link" href="#/pointinfo?type=block&dpu=${encodeURIComponent(b.dpu)}&name=${encodeURIComponent(b.name)}">${esc(b.name)}</a></td>
      <td>${esc(b.dpu)}</td></tr>`).join("") || `<tr><td class="dim">（空）在列表中点 ★ 收藏</td></tr>`}
    </tbody></table>
    <h3>收藏的点（${points.length}）</h3>
    <table class="grid"><tbody id="fav-points">${points.map(n => `<tr>
      <td style="width:30px"><span class="fav on" data-kind="point" data-name="${esc(n)}">★</span></td>
      <td class="mono"><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(n)}">${esc(n)}</a></td>
      <td class="num fav-val" data-name="${esc(n)}">…</td></tr>`).join("") || `<tr><td class="dim">（空）</td></tr>`}
    </tbody></table>`;

  $$(".fav", body).forEach(f => f.onclick = () => {
    if (f.dataset.kind === "block") favStore.toggleBlock(f.dataset.dpu, f.dataset.name);
    else favStore.togglePoint(f.dataset.name);
    renderFavorites(body);
  });

  // 拉取收藏点的实时值（限量）
  points.slice(0, 60).forEach(async n => {
    try {
      const d = await api(`/point/${encodeURIComponent(n)}`);
      const cell = $(`.fav-val[data-name="${CSS.escape(n)}"]`, body);
      if (cell) cell.textContent = fmtVal(d.value);
    } catch { }
  });
}

// ------------------------- 视图：PointInfo -------------------------
views.pointinfo = (main, query) => {
  if (query.type === "block" && query.dpu && query.name) renderBlockInfo(main, query.dpu, query.name);
  else if (query.type === "point" && query.name) renderPointInfo(main, query.name);
  else {
    main.innerHTML = `
      <h2>PointInfo</h2>
      <div class="toolbar">
        <input id="pi-name" placeholder="输入点名或块名…" style="width:280px">
        <button id="pi-go" class="btn btn-primary">打开</button>
      </div>
      <p class="mini-note">独立检视工具：输入名字自动识别点/块；也可以从“点与功能块”列表跳转。</p>`;
    $("#pi-go").onclick = open;
    $("#pi-name").onkeydown = e => { if (e.key === "Enter") open(); };
    async function open() {
      const name = $("#pi-name").value.trim();
      if (!name) return;
      try {
        const b = await api(`/blockfind/${encodeURIComponent(name)}`);
        location.hash = `#/pointinfo?type=block&dpu=${encodeURIComponent(b.dpu)}&name=${encodeURIComponent(b.name)}`;
      } catch {
        try {
          await api(`/point/${encodeURIComponent(name)}`);
          location.hash = `#/pointinfo?type=point&name=${encodeURIComponent(name)}`;
        } catch (e) { toast(`找不到点或块：${name}`, "error"); }
      }
    }
  }
};

function xrefLinks(list, kind) {
  if (!list || !list.length) return `<span class="dim">（无）</span>`;
  return `<ul class="xref-list">` + list.map(x =>
    `<li>${x.isDead ? '<span class="badge badge-dim">死</span> ' : ""}<a class="link" href="#/pointinfo?type=block&dpu=${encodeURIComponent(x.dpuName)}&name=${encodeURIComponent(x.blockName)}">${esc(x.blockName)}</a><span class="dim">.${esc(x.pinName)}${x.reversed ? " (~)" : ""} [${esc(x.fcName)}] @${esc(x.dpuName)}</span></li>`).join("") + `</ul>`;
}

async function renderBlockInfo(main, dpu, name) {
  let detail;
  try { detail = await api(`/block/${encodeURIComponent(dpu)}/${encodeURIComponent(name)}`); }
  catch (e) { main.innerHTML = `<h2>PointInfo</h2><p class="dim">${esc(e.message)}</p>`; return; }

  const isFav = favStore.hasBlock(dpu, name);
  main.innerHTML = `
    <div class="pi-head">
      <span class="fav ${isFav ? "on" : ""}" id="pi-fav" style="font-size:17px">★</span>
      <span class="title">${esc(detail.name)}</span>
      <span class="kv">功能码 <b>${esc(detail.fc)}</b></span>
      <span class="kv">DPU <b>${esc(detail.dpu)}</b></span>
      <span class="kv">状态区 <b>${detail.stateBytes} B</b></span>
      <span class="spacer"></span>
      <label><input type="checkbox" id="pi-auto" checked> 自动刷新</label>
    </div>
    <div class="tabs">
      <div class="tab active" data-tab="inputs">输入管脚（${detail.inputs.length}）</div>
      <div class="tab" data-tab="outputs">输出管脚（${detail.outputs.length}）</div>
      <div class="tab" data-tab="constants">规格数（${detail.constants.length}）</div>
      <div class="tab" data-tab="internals">内部变量（${detail.internals.length}）</div>
    </div>
    <div id="pi-body"></div>`;

  $("#pi-fav").onclick = () => {
    const on = favStore.toggleBlock(dpu, name);
    $("#pi-fav").classList.toggle("on", on);
  };

  let tab = "inputs";
  $$(".tab", main).forEach(t => t.onclick = () => {
    tab = t.dataset.tab;
    $$(".tab", main).forEach(x => x.classList.toggle("active", x === t));
    draw();
  });

  function draw() {
    const body = $("#pi-body");
    if (!body) return;
    if (tab === "inputs") {
      body.innerHTML = `<table class="grid"><thead><tr>
        <th>管脚</th><th>类型</th><th class="num">当前值</th><th>连接点</th><th>交叉引用（源头）</th><th>强制</th><th>强制值</th><th></th>
      </tr></thead><tbody>` + detail.inputs.map(p => `<tr class="${p.forced ? "force-on" : ""}">
        <td class="mono">${esc(p.pin)}</td>
        <td>${esc(p.type)}</td>
        <td class="num">${fmtVal(p.value)}</td>
        <td class="mono">${p.point ? `<a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(p.point)}">${p.reversed ? "~" : ""}${esc(p.point)}</a>${p.dead ? ' <span class="badge badge-dim">死绑定</span>' : ""}` : '<span class="dim">-</span>'}</td>
        <td>${xrefLinks(p.sources)}</td>
        <td><input type="checkbox" class="f-on" data-pin="${esc(p.pin)}" ${p.forced ? "checked" : ""}></td>
        <td><input class="cell-edit f-val" data-pin="${esc(p.pin)}" value="${p.forceValue !== null && p.forceValue !== undefined ? fmtVal(p.forceValue) : ""}"></td>
        <td><button class="btn btn-mini f-apply" data-pin="${esc(p.pin)}">应用</button></td>
      </tr>`).join("") + `</tbody></table>
      <p class="mini-note">强制：勾选 + 强制值 + 应用 ⇒ 管脚每周期被钉在强制值；取消勾选 + 应用 ⇒ 解除并恢复强制前值。</p>`;
      bindForce(body);
    } else if (tab === "outputs") {
      body.innerHTML = `<table class="grid"><thead><tr>
        <th>管脚</th><th>类型</th><th class="num">当前值</th><th>目标点 / 使用方</th><th>强制</th><th>强制值</th><th></th>
      </tr></thead><tbody>` + detail.outputs.map(p => `<tr class="${p.forced ? "force-on" : ""}">
        <td class="mono">${esc(p.pin)}</td>
        <td>${esc(p.type)}</td>
        <td class="num">${fmtVal(p.value)}</td>
        <td>${p.targets.length ? p.targets.map(t =>
          `<div class="mono"><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(t.point)}">${t.reversed ? "~" : ""}${esc(t.point)}</a>${t.dead ? ' <span class="badge badge-dim">死绑定</span>' : ""}
           ${t.consumers.length ? `<a class="link xref-pop" data-point="${esc(t.point)}">（${t.consumers.length} 处使用）</a>` : '<span class="dim">（无使用方）</span>'}</div>`).join("") : '<span class="dim">-</span>'}</td>
        <td><input type="checkbox" class="f-on" data-pin="${esc(p.pin)}" ${p.forced ? "checked" : ""}></td>
        <td><input class="cell-edit f-val" data-pin="${esc(p.pin)}" value="${p.forceValue !== null && p.forceValue !== undefined ? fmtVal(p.forceValue) : ""}"></td>
        <td><button class="btn btn-mini f-apply" data-pin="${esc(p.pin)}">应用</button></td>
      </tr>`).join("") + `</tbody></table>`;
      bindForce(body);
      $$(".xref-pop", body).forEach(a => a.onclick = () => showXrefModal(a.dataset.point));
    } else {
      const rows = tab === "constants" ? detail.constants : detail.internals;
      body.innerHTML = `<table class="grid"><thead><tr>
        <th>名称</th><th>类型</th><th class="num">当前值</th><th>新值</th><th></th>
      </tr></thead><tbody>` + rows.map(r => `<tr>
        <td class="mono">${esc(r.name)}</td>
        <td>${esc(r.type)}</td>
        <td class="num">${fmtVal(r.value)}</td>
        <td>${r.writable ? `<input class="cell-edit w-val" data-field="${esc(r.name)}">` : '<span class="dim">只读</span>'}</td>
        <td>${r.writable ? `<button class="btn btn-mini w-apply" data-field="${esc(r.name)}">写入</button>` : ""}</td>
      </tr>`).join("") + `</tbody></table>
      <p class="mini-note">${tab === "constants" ? "规格数在线修改立即生效；注意：在线下装会用工程库中的参数覆盖在线修改。" : "内部变量为块的运行状态，修改需谨慎。"}</p>`;
      $$(".w-apply", body).forEach(b => b.onclick = () => {
        const val = $(`.w-val[data-field="${CSS.escape(b.dataset.field)}"]`, body).value;
        if (val === "") return toast("请输入新值", "error");
        api(`/block/${encodeURIComponent(dpu)}/${encodeURIComponent(name)}/field`, { method: "PUT", body: { field: b.dataset.field, value: val } })
          .then(d => { detail = d; toast(`${b.dataset.field} 已写入`, "ok", 1500); draw(); })
          .catch(e => toast(e.message, "error"));
      });
      $$(".w-val", body).forEach(inp => inp.onkeydown = e => {
        if (e.key === "Enter") $(`.w-apply[data-field="${CSS.escape(inp.dataset.field)}"]`, body).click();
      });
    }
  }

  function bindForce(body) {
    $$(".f-apply", body).forEach(b => b.onclick = () => {
      const pin = b.dataset.pin;
      const on = $(`.f-on[data-pin="${CSS.escape(pin)}"]`, body).checked;
      const val = $(`.f-val[data-pin="${CSS.escape(pin)}"]`, body).value || "0";
      api(`/block/${encodeURIComponent(dpu)}/${encodeURIComponent(name)}/force`, { method: "POST", body: { pin, forced: on, value: val } })
        .then(() => { toast(on ? `${pin} 已强制 = ${val}` : `${pin} 已解除强制`, "ok", 1600); refresh(); })
        .catch(e => toast(e.message, "error"));
    });
  }

  async function refresh() {
    try {
      detail = await api(`/block/${encodeURIComponent(dpu)}/${encodeURIComponent(name)}`);
      draw();
    } catch { }
  }

  draw();
  window.__viewRefresh = refresh;
  setViewTimer(() => { if ($("#pi-auto") && $("#pi-auto").checked) refresh(); refreshTopbar(); }, 1200);
}

function showXrefModal(point) {
  api(`/xref/${encodeURIComponent(point)}`).then(x => {
    modal(`交叉引用：${point}`, `
      <h3 style="margin-top:0">源头（写入方 ${x.producers.length}）</h3>${xrefLinks(x.producers)}
      <h3>使用方（读取方 ${x.consumers.length}）</h3>${xrefLinks(x.consumers)}
      <p class="mini-note"><a class="link" href="#/pointinfo?type=point&name=${encodeURIComponent(point)}">打开点 PointInfo →</a></p>`,
      (mask) => { $$("a.link", mask).forEach(a => a.addEventListener("click", () => mask.remove())); });
  }).catch(e => toast(e.message, "error"));
}

async function renderPointInfo(main, name) {
  let d;
  try { d = await api(`/point/${encodeURIComponent(name)}`); }
  catch (e) { main.innerHTML = `<h2>PointInfo</h2><p class="dim">${esc(e.message)}</p>`; return; }

  const isFav = favStore.hasPoint(name);
  main.innerHTML = `
    <div class="pi-head">
      <span class="fav ${isFav ? "on" : ""}" id="pi-fav" style="font-size:17px">★</span>
      <span class="title">${esc(d.name)}</span>
      <span class="kv">类型 <b>${d.kind}</b></span>
      <span class="kv">DPU <b>${esc(d.dpu)}</b></span>
      <span class="kv">当前值 <b id="pv-now">${fmtVal(d.value)}</b></span>
      <span class="spacer"></span>
      <label><input type="checkbox" id="pi-auto" checked> 自动刷新</label>
    </div>
    <div class="toolbar">
      <label>写值</label><input id="pv-set" class="cell-edit" style="width:130px">
      <button id="pv-apply" class="btn btn-mini btn-primary">写入</button>
      <span style="width:20px"></span>
      <label>点强制</label><input type="checkbox" id="pf-on">
      <input id="pf-val" class="cell-edit" style="width:110px" placeholder="强制值">
      <button id="pf-apply" class="btn btn-mini">应用强制</button>
    </div>
    <div class="tabs">
      <div class="tab active" data-tab="fields">字段</div>
      <div class="tab" data-tab="xref">交叉引用</div>
      <div class="tab" data-tab="history">历史曲线</div>
    </div>
    <div id="pt-body"></div>`;

  $("#pi-fav").onclick = () => {
    const on = favStore.togglePoint(name);
    $("#pi-fav").classList.toggle("on", on);
  };
  $("#pv-apply").onclick = () => {
    const v = $("#pv-set").value;
    if (v === "") return;
    api(`/point/${encodeURIComponent(name)}/value`, { method: "PUT", body: { value: v } })
      .then(r => { toast(`已写入 ${fmtVal(r.value)}`, "ok", 1500); refresh(); })
      .catch(e => toast(e.message, "error"));
  };
  $("#pf-apply").onclick = () => {
    api(`/point/${encodeURIComponent(name)}/force`, {
      method: "POST",
      body: { forced: $("#pf-on").checked, value: $("#pf-val").value || null },
    }).then(() => { toast("强制状态已更新", "ok", 1500); refresh(); })
      .catch(e => toast(e.message, "error"));
  };

  let tab = "fields";
  $$(".tab", main).forEach(t => t.onclick = () => {
    tab = t.dataset.tab;
    $$(".tab", main).forEach(x => x.classList.toggle("active", x === t));
    draw();
  });

  function draw() {
    const body = $("#pt-body");
    if (!body) return;
    if (tab === "fields") {
      body.innerHTML = `<table class="grid"><thead><tr>
        <th>字段</th><th>类型</th><th class="num">值</th><th>新值</th><th></th>
      </tr></thead><tbody>` + d.fields.map(f => `<tr>
        <td class="mono">${esc(f.name)}</td><td>${esc(f.type)}</td>
        <td class="num">${fmtVal(f.value)}</td>
        <td><input class="cell-edit pf-field" data-field="${esc(f.name)}"></td>
        <td><button class="btn btn-mini pf-fapply" data-field="${esc(f.name)}">写</button></td>
      </tr>`).join("") + `</tbody></table>
      <p class="mini-note">直写内存字段（不触发报警重算副作用）；写 buffer 建议用上方“写值”。</p>`;
      $$(".pf-fapply", body).forEach(b => b.onclick = () => {
        const val = $(`.pf-field[data-field="${CSS.escape(b.dataset.field)}"]`, body).value;
        if (val === "") return;
        api(`/point/${encodeURIComponent(name)}/field`, { method: "PUT", body: { field: b.dataset.field, value: val } })
          .then(() => { toast(`${b.dataset.field} 已写入`, "ok", 1500); refresh(); })
          .catch(e => toast(e.message, "error"));
      });
    } else if (tab === "xref") {
      body.innerHTML = `
        <h3 style="margin-top:0">源头（写入方 ${d.producers.length}）</h3>${xrefLinks(d.producers)}
        <h3>使用方（读取方 ${d.consumers.length}）</h3>${xrefLinks(d.consumers)}`;
    } else {
      body.innerHTML = `<div class="toolbar">
          <button id="h-load" class="btn btn-mini">加载最近 500 条</button><span id="h-info" class="dim"></span>
        </div><div id="h-plot"></div>`;
      $("#h-load").onclick = async () => {
        try {
          const h = await api(`/history/query?point=${encodeURIComponent(name)}&max=500`);
          $("#h-info").textContent = `共 ${h.total} 条记录`;
          drawSpark($("#h-plot"), h.samples);
        } catch (e) { $("#h-info").textContent = e.message; }
      };
    }
  }

  function drawSpark(el, samples) {
    if (!samples.length) { el.innerHTML = `<p class="dim">（无记录）</p>`; return; }
    const w = 860, h = 220, pad = 34;
    const vs = samples.map(s => s.value);
    const min = Math.min(...vs), max = Math.max(...vs);
    const span = (max - min) || 1;
    const pts = samples.map((s, i) => {
      const x = pad + (w - 2 * pad) * i / Math.max(1, samples.length - 1);
      const y = h - pad - (h - 2 * pad) * (s.value - min) / span;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    }).join(" ");
    el.innerHTML = `<svg width="${w}" height="${h}" style="background:var(--bg2);border:1px solid var(--line);border-radius:6px">
      <text x="6" y="16" fill="var(--fg-dim)" font-size="11">${fmtVal(max)}</text>
      <text x="6" y="${h - 6}" fill="var(--fg-dim)" font-size="11">${fmtVal(min)}</text>
      <polyline points="${pts}" fill="none" stroke="var(--accent)" stroke-width="1.6"/>
    </svg>`;
  }

  async function refresh() {
    try {
      d = await api(`/point/${encodeURIComponent(name)}`);
      $("#pv-now").textContent = fmtVal(d.value);
      if (tab === "fields") draw();
    } catch { }
  }

  draw();
  window.__viewRefresh = refresh;
  setViewTimer(() => { if ($("#pi-auto") && $("#pi-auto").checked) refresh(); refreshTopbar(); }, 1200);
}

// ------------------------- 视图：工况与快照 -------------------------
views.store = (main) => {
  main.innerHTML = `
    <h2>工况与快照</h2>
    <div class="cards">
      <div class="card" style="flex:1">
        <div class="k">工况（condition）＝ 工程库副本 + 全量数据镜像。任何工程演化后都能完整重现（加载 = 重装工程 + 回放数据）。</div>
      </div>
      <div class="card" style="flex:1">
        <div class="k">快照（snapshot）＝ 仅变化的点数据 + 块内部状态（相对装配基线），保存快、体积小。工程变更后加载自动进入按名兼容转换。</div>
      </div>
    </div>

    <h3>工况</h3>
    <div class="toolbar">
      <input id="c-name" placeholder="工况名称" style="width:200px">
      <input id="c-comment" placeholder="备注（可选）" style="width:260px">
      <button id="c-save" class="btn btn-primary">保存当前工况</button>
    </div>
    <table class="grid"><thead><tr>
      <th>名称</th><th>保存时间</th><th class="num">大小</th><th>工程指纹</th><th>版本</th><th>备注</th><th style="width:170px"></th>
    </tr></thead><tbody id="c-rows"></tbody></table>

    <h3>快照</h3>
    <div class="toolbar">
      <input id="s-name" placeholder="快照名称" style="width:200px">
      <input id="s-comment" placeholder="备注（可选）" style="width:260px">
      <button id="s-save" class="btn btn-primary">保存当前快照</button>
    </div>
    <table class="grid"><thead><tr>
      <th>名称</th><th>保存时间</th><th class="num">大小</th><th>工程指纹</th><th>版本</th><th>备注</th><th style="width:170px"></th>
    </tr></thead><tbody id="s-rows"></tbody></table>`;

  function rowHtml(e, kind) {
    return `<tr>
      <td class="mono">${esc(e.name)}</td>
      <td>${fmtTime(e.savedAtUtc)}</td>
      <td class="num">${fmtBytes(e.sizeBytes)}</td>
      <td class="mono">${esc(e.fingerprint)} ${e.matchesCurrent ? '<span class="badge badge-ok">当前工程</span>' : '<span class="badge badge-warn">工程已变</span>'}</td>
      <td class="num">v${e.projectVersion}</td>
      <td>${esc(e.comment || "")}</td>
      <td>
        <button class="btn btn-mini btn-primary act-load" data-kind="${kind}" data-name="${esc(e.name)}">加载</button>
        <button class="btn btn-mini btn-danger act-del" data-kind="${kind}" data-name="${esc(e.name)}">删除</button>
      </td>
    </tr>`;
  }

  async function refresh() {
    try {
      const [conds, snaps] = await Promise.all([api("/store/conditions"), api("/store/snapshots")]);
      $("#c-rows").innerHTML = conds.map(e => rowHtml(e, "conditions")).join("") || `<tr><td class="dim" colspan="7">（无）</td></tr>`;
      $("#s-rows").innerHTML = snaps.map(e => rowHtml(e, "snapshots")).join("") || `<tr><td class="dim" colspan="7">（无）</td></tr>`;
      bind();
    } catch (e) { toast(e.message, "error"); }
  }

  function bind() {
    $$(".act-load").forEach(b => b.onclick = async () => {
      const { kind, name } = b.dataset;
      b.disabled = true;
      try {
        const r = await api(`/store/${kind}/${encodeURIComponent(name)}/load`, { method: "POST" });
        if (kind === "snapshots" && r.compatMode) {
          modal("快照兼容加载报告", `
            <p>${esc(r.summary)}</p>
            <table class="grid">
              <tr><td>点回放</td><td class="num">${r.pointsApplied.toLocaleString()}</td><td>点跳过</td><td class="num">${r.pointsSkipped.toLocaleString()}</td></tr>
              <tr><td>块直拷</td><td class="num">${r.blocksRawCopied.toLocaleString()}</td><td>块字段转换</td><td class="num">${r.blocksFieldConverted.toLocaleString()}</td></tr>
              <tr><td>块跳过</td><td class="num">${r.blocksSkipped.toLocaleString()}</td><td></td><td></td></tr>
            </table>
            ${r.messages.length ? `<h3>提示</h3><ul>${r.messages.map(m => `<li>${esc(m)}</li>`).join("")}</ul>` : ""}`);
        } else {
          toast(`${kind === "conditions" ? "工况" : "快照"} [${name}] 已加载`, "ok");
        }
        refreshTopbar();
      } catch (e) { toast(e.message, "error"); }
      b.disabled = false;
    });
    $$(".act-del").forEach(b => b.onclick = async () => {
      const { kind, name } = b.dataset;
      if (!confirm(`确认删除 ${kind === "conditions" ? "工况" : "快照"} [${name}]？`)) return;
      try {
        await api(`/store/${kind}/${encodeURIComponent(name)}`, { method: "DELETE" });
        toast("已删除", "ok", 1500);
        refresh();
      } catch (e) { toast(e.message, "error"); }
    });
  }

  $("#c-save").onclick = async () => {
    const name = $("#c-name").value.trim() || `工况-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "").slice(0, 14)}`;
    try {
      $("#c-save").disabled = true;
      await api("/store/conditions", { method: "POST", body: { name, comment: $("#c-comment").value || null } });
      toast(`工况 [${name}] 已保存`, "ok");
      refresh();
    } catch (e) { toast(e.message, "error"); }
    $("#c-save").disabled = false;
  };
  $("#s-save").onclick = async () => {
    const name = $("#s-name").value.trim() || `快照-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "").slice(0, 14)}`;
    try {
      $("#s-save").disabled = true;
      const r = await api("/store/snapshots", { method: "POST", body: { name, comment: $("#s-comment").value || null } });
      toast(`快照 [${name}] 已保存（${r.changedSlots.toLocaleString()}/${r.totalSlots.toLocaleString()} 槽变化）`, "ok");
      refresh();
    } catch (e) { toast(e.message, "error"); }
    $("#s-save").disabled = false;
  };

  refresh();
  window.__viewRefresh = refresh;
};

// ------------------------- 视图：在线下装 -------------------------
views.download = (main) => {
  main.innerHTML = `
    <h2>在线下装</h2>
    <p class="mini-note">流程：选择新工程库 → <b>预检</b>（差异报告）→ 确认 → <b>提交下装</b>（自动备份工况 → 状态按名迁移 → 周期边界原子切换，运行中自动恢复）。</p>
    <div class="toolbar">
      <label>新工程库路径</label>
      <input id="dl-mdb" style="width:440px" placeholder="D:\\path\\to\\project.mdb">
      <button id="dl-prepare" class="btn btn-primary">预检</button>
    </div>
    <div id="dl-report"></div>`;

  const st = window.__status;
  if (st && st.project) $("#dl-mdb").value = st.project.mdbPath;

  $("#dl-prepare").onclick = async () => {
    const mdbPath = $("#dl-mdb").value.trim();
    if (!mdbPath) return toast("请输入工程库路径", "error");
    $("#dl-prepare").disabled = true;
    try {
      const plan = await api("/download/prepare", { method: "POST", body: { mdbPath } });
      renderPlan(plan);
    } catch (e) { toast(e.message, "error"); }
    $("#dl-prepare").disabled = false;
  };

  function renderPlan(plan) {
    const s = plan.summary;
    const box = $("#dl-report");
    box.innerHTML = `
      <h3>差异报告（计划 ${plan.planId}）</h3>
      <div class="cards">
        <div class="card"><div class="k">指纹</div><div class="v"><small>旧</small> ${plan.oldFingerprint}<br><small>新</small> ${plan.newFingerprint}</div></div>
        <div class="card"><div class="k">点</div><div class="v">+${s.pointsAdded} / -${s.pointsRemoved} / ~${s.pointsChanged}</div></div>
        <div class="card"><div class="k">块</div><div class="v">+${s.blocksAdded} / -${s.blocksRemoved} / 类型变更 ${s.blocksTypeChanged}</div></div>
        <div class="card"><div class="k">接线 / 参数</div><div class="v">${s.blocksWiringChanged} / ${s.blocksParamChanged}</div></div>
        <div class="card"><div class="k">控制器</div><div class="v">+${s.controllersAdded} / -${s.controllersRemoved}</div></div>
      </div>
      ${plan.identical ? `<p class="badge badge-ok">新工程与当前工程完全一致（仍可下装，等效重建）</p>` : ""}
      ${s.destructive ? `<p class="diff-destructive">⚠ 含破坏性变更（删除/类型变化）：相关实体的运行状态将丢失，请确认。</p>` : ""}
      ${(plan.errors || []).length ? `<div class="diff-destructive"><p>✖ 预检发现 ${plan.errors.length} 个致命引用问题，无法提交下装（请先修正工程库）：</p>
        <ul>${plan.errors.slice(0, 20).map(e => `<li>${esc(e)}</li>`).join("")}</ul></div>` : ""}
      <div class="toolbar">
        <select id="dl-filter"><option value="">全部差异（${plan.totalEntries}）</option></select>
        <span class="spacer"></span>
        <label><input type="checkbox" id="dl-backup" checked> 下装前自动备份工况</label>
        <button id="dl-commit" class="btn btn-danger" ${(plan.errors || []).length ? "disabled" : ""}>提交下装</button>
      </div>
      <table class="grid"><thead><tr><th>类别</th><th>控制器</th><th>对象</th><th>明细</th></tr></thead>
      <tbody id="dl-entries"></tbody></table>
      ${plan.entriesTruncated ? `<p class="mini-note">（差异条目过多，仅显示前 500 条）</p>` : ""}
      <div id="dl-result"></div>`;

    const kinds = [...new Set(plan.entries.map(e => e.kind))];
    $("#dl-filter").innerHTML += kinds.map(k => `<option>${k}</option>`).join("");
    const kindName = k => ({
      PointAdded: "点新增", PointRemoved: "点删除", PointChanged: "点变更",
      BlockAdded: "块新增", BlockRemoved: "块删除", BlockTypeChanged: "块类型变更",
      BlockWiringChanged: "接线变更", BlockParamChanged: "参数变更",
      ControllerAdded: "控制器新增", ControllerRemoved: "控制器删除",
    }[k] || k);

    function drawEntries() {
      const f = $("#dl-filter").value;
      $("#dl-entries").innerHTML = plan.entries
        .filter(e => !f || e.kind === f)
        .map(e => `<tr>
          <td><span class="${e.destructive ? "diff-destructive" : ""}">${kindName(e.kind)}</span></td>
          <td>${esc(e.controller)}</td>
          <td class="mono">${esc(e.name)}</td>
          <td>${esc(e.detail)}</td>
        </tr>`).join("") || `<tr><td colspan="4" class="dim">（无差异）</td></tr>`;
    }
    $("#dl-filter").onchange = drawEntries;
    drawEntries();

    $("#dl-commit").onclick = async () => {
      if (s.destructive && !confirm("含破坏性变更，相关状态将丢失。确认继续下装？")) return;
      $("#dl-commit").disabled = true;
      $("#dl-result").innerHTML = `<p class="dim">下装中（重建 + 迁移 + 切换，秒级）…</p>`;
      try {
        const r = await api("/download/commit", { method: "POST", body: { planId: plan.planId, backup: $("#dl-backup").checked } });
        $("#dl-result").innerHTML = `
          <h3>下装完成 → v${r.version}（指纹 ${r.fingerprint}）</h3>
          <table class="grid" style="max-width:640px">
            <tr><td>点保留</td><td class="num">${r.pointsPreserved.toLocaleString()}</td><td>点新增 / 删除 / 类型变更</td><td class="num">${r.pointsNew} / ${r.pointsDropped} / ${r.pointsTypeChanged}</td></tr>
            <tr><td>块保留</td><td class="num">${r.blocksPreserved.toLocaleString()}</td><td>块新增 / 删除 / 类型变更</td><td class="num">${r.blocksNew} / ${r.blocksDropped} / ${r.blocksTypeChanged}</td></tr>
            <tr><td>状态字段转移</td><td class="num">${r.fieldsTransferred.toLocaleString()}</td><td>强制携带 / 迁移耗时</td><td class="num">${r.forcesCarried} / ${r.transferMs.toFixed(0)} ms</td></tr>
          </table>
          ${r.messages.length ? `<ul>${r.messages.map(m => `<li>${esc(m)}</li>`).join("")}</ul>` : ""}`;
        toast("在线下装完成", "ok");
        refreshTopbar();
      } catch (e) {
        $("#dl-result").innerHTML = `<p class="diff-destructive">下装失败：${esc(e.message)}</p>`;
      }
      $("#dl-commit").disabled = false;
    };
  }
};

// ------------------------- 视图：版本档案 -------------------------
views.versions = (main) => {
  main.innerHTML = `<h2>工程版本档案</h2>
    <table class="grid"><thead><tr>
      <th class="num">版本</th><th>指纹</th><th>来源</th><th>时间</th><th>工程库</th><th>备注</th>
    </tr></thead><tbody id="v-rows"></tbody></table>`;
  api("/project/versions").then(vs => {
    const srcName = s => ({ load: "装载", download: "在线下装", condition: "工况加载" }[s] || s);
    $("#v-rows").innerHTML = vs.slice().reverse().map(v => `<tr>
      <td class="num">v${v.version}</td>
      <td class="mono">${esc(v.fingerprint)}</td>
      <td>${srcName(v.source)}</td>
      <td>${fmtTime(v.timeUtc)}</td>
      <td class="mono">${esc(v.mdbPath)}</td>
      <td>${esc(v.comment || "")}</td>
    </tr>`).join("") || `<tr><td colspan="6" class="dim">（空）</td></tr>`;
  }).catch(e => toast(e.message, "error"));
};

// ------------------------- 视图：日志 -------------------------
let logSource = null;
views.logs = (main) => {
  main.innerHTML = `
    <h2>运行日志</h2>
    <div class="toolbar">
      <select id="log-level">
        <option value="">全部级别</option><option>Info</option><option>Warn</option><option>Error</option>
      </select>
      <label><input type="checkbox" id="log-scroll" checked> 自动滚动</label>
      <button id="log-clear" class="btn btn-mini">清屏</button>
      <span class="spacer"></span><span id="log-count" class="dim"></span>
    </div>
    <div class="log-view" id="log-view"></div>`;

  const view = $("#log-view");
  let count = 0;

  function push(e) {
    const lv = $("#log-level").value;
    if (lv && e.level !== lv) return;
    const div = document.createElement("div");
    div.className = "log-line " + (e.level === "Warn" ? "warn" : e.level === "Error" ? "err" : "");
    div.innerHTML = `<span class="t">${esc(e.time)}</span> <span class="s">[${esc(e.source)}]</span> ${esc(e.message)}`;
    view.appendChild(div);
    while (view.childElementCount > 3000) view.firstElementChild.remove();
    count++;
    $("#log-count").textContent = `${count} 条`;
    if ($("#log-scroll").checked) view.scrollTop = view.scrollHeight;
  }

  $("#log-clear").onclick = () => { view.innerHTML = ""; count = 0; };

  if (logSource) logSource.close();
  logSource = new EventSource("/api/logs/stream");
  logSource.onmessage = ev => push(JSON.parse(ev.data));
  logSource.onerror = () => { /* 自动重连 */ };
};

// ------------------------- 启动 -------------------------
bindTopbar();
window.addEventListener("hashchange", navigate);
refreshTopbar().then(navigate);
setInterval(refreshTopbar, 3000);
