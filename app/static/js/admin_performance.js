(() => {
  const panel = document.querySelector('[data-performance-dashboard]');
  if (!panel) return;
  const status = panel.querySelector('[data-performance-status]');
  const range = panel.querySelector('[data-performance-range]');
  const channel = panel.querySelector('[data-performance-channel]');
  const refresh = panel.querySelector('[data-performance-refresh]');
  const sourceName = source => source === 'server' ? '服务端' : '客户端页面';
  const ms = value => `${Number(value).toLocaleString('zh-CN', {maximumFractionDigits: 1})} ms`;
  const date = value => new Date(value * 1000).toLocaleString('zh-CN', {
    timeZone: 'Asia/Shanghai', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
  });
  const element = (tag, text, className) => {
    const node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (className) node.className = className;
    return node;
  };
  function table(target, headings, rows) {
    target.replaceChildren();
    if (!rows.length) { target.append(element('p', '暂无样本', 'admin-performance-empty')); return; }
    const table = element('table'), head = element('thead'), tr = element('tr'), body = element('tbody');
    headings.forEach(heading => tr.append(element('th', heading)));
    head.append(tr);
    rows.forEach(values => {
      const row = element('tr');
      values.forEach(value => row.append(element('td', value)));
      body.append(row);
    });
    table.append(head, body);
    target.append(table);
  }
  function render(data) {
    const channels = panel.querySelector('[data-performance-channels]');
    channels.replaceChildren();
    for (const row of data.channels || []) {
      const card = element('button', undefined, 'admin-channel-card');
      card.type = 'button';
      card.setAttribute('aria-pressed', String(channel.value === row.key));
      card.append(element('span', row.label), element('strong', row.requests),
        element('small', `请求样本 · 错误 ${row.errors} · P95 ${ms(row.p95)}`));
      card.addEventListener('click', () => { channel.value = row.key; load(); });
      channels.append(card);
    }
    const requests = data.groups.filter(row => row.metric === 'request');
    const totals = panel.querySelector('[data-performance-totals]');
    totals.replaceChildren();
    for (const [label, value] of [['服务端请求样本', data.server_count], ['客户端页面请求样本', data.browser_count],
      ['服务端错误样本', data.errors], ['主线程长任务', data.longtasks]]) {
      const card = element('div', undefined, 'admin-total');
      card.append(element('span', label), element('strong', value));
      totals.append(card);
    }
    const nodes = panel.querySelector('[data-performance-nodes]');
    nodes.replaceChildren();
    const maximum = Math.max(1, ...requests.map(row => row.p95));
    for (const row of requests.slice(0, 12)) {
      const item = element('div', undefined, 'admin-performance-node');
      const heading = element('div');
      heading.append(element('span', `${row.channel_label} · ${row.node} · ${sourceName(row.source)}`), element('strong', ms(row.p95)));
      const track = element('div', undefined, 'admin-performance-track');
      const bar = element('i', undefined, row.source);
      bar.style.width = `${Math.max(.5, row.p95 * 100 / maximum)}%`;
      track.append(bar);
      item.append(heading, track);
      nodes.append(item);
    }
    if (!requests.length) nodes.append(element('p', '暂无请求样本', 'admin-performance-empty'));
    const trend = panel.querySelector('[data-performance-trend]');
    trend.replaceChildren();
    if (data.trend.some(row => row.count)) {
      const chart = element('div', undefined, 'admin-performance-trend');
      chart.setAttribute('role', 'img');
      chart.setAttribute('aria-label', '请求 P95 趋势，青色服务端，蓝色客户端页面，悬停查看时段和数值');
      const peak = Math.max(1, ...data.trend.flatMap(row => [row.server, row.browser]));
      data.trend.forEach((row, index) => {
        const column = element('div');
        column.title = `${date(row.received)} · ${row.count} 条 · 服务端 ${ms(row.server)} · 客户端页面 ${ms(row.browser)}`;
        const bars = element('div');
        for (const source of ['server', 'browser']) {
          const bar = element('i', undefined, source);
          bar.style.height = `${row[source] * 100 / peak}%`;
          bars.append(bar);
        }
        column.append(bars, element('small', index % 6 === 0 || index === 23 ? date(row.received).slice(-5) : ''));
        chart.append(column);
      });
      trend.append(chart);
    } else trend.append(element('p', '暂无趋势数据', 'admin-performance-empty'));
    const columns = ['渠道', '节点', '采集端', '样本', 'P50', 'P95', '最大', '错误'];
    const values = row => [row.channel_label, row.node, sourceName(row.source), row.count, ms(row.p50), ms(row.p95), ms(row.max), row.errors];
    table(panel.querySelector('[data-performance-requests]'), columns, requests.map(values));
    table(panel.querySelector('[data-performance-phases]'),
      ['渠道', '节点', '阶段', '样本', 'P50', 'P95', '最大'], data.groups.filter(row => row.source === 'server' && row.metric !== 'request')
        .map(row => [row.channel_label, row.node, row.metric_label, row.count, ms(row.p50), ms(row.p95), ms(row.max)]));
    table(panel.querySelector('[data-performance-page]'),
      ['渠道', '节点', '指标', '样本', 'P50', 'P95', '最大'], data.groups.filter(row => row.source === 'browser' && row.metric !== 'request')
        .map(row => [row.channel_label, row.node, row.metric_label, row.count, ms(row.p50), ms(row.p95), ms(row.max)]));
    table(panel.querySelector('[data-performance-slow]'), ['接收时间', '渠道', '节点', '采集端', '指标', '耗时', '状态'],
      data.slow.map(row => [date(row.received), row.channel_label, row.node, sourceName(row.source), row.metric, ms(row.duration),
        row.metric_key === 'request' ? row.status === 0 ? '网络失败' : row.status : '—']));
    const health = data.persisted;
    const cumulative = health
      ? ` · 落盘累计已写入 ${Number(health.samples).toLocaleString('zh-CN')} 条 · 累计丢弃 ${Number(health.dropped).toLocaleString('zh-CN')} 条` +
        `${health.last_drop ? `（最近 ${date(health.last_drop)}）` : ''} · 累计写入失败 ${Number(health.write_failures).toLocaleString('zh-CN')} 次` +
        `${health.last_failure ? `（最近 ${date(health.last_failure)}）` : ''}`
      : ' · 尚未落盘健康数据（本进程还没成功写入）';
    status.textContent = !data.enabled ? '性能采集已关闭。' :
      `${data.ready ? '采集存储正常' : '存储暂不可用或正在初始化'} · 排队 ${data.queued} 条 · 本进程写入 ${data.written} 条 · 本进程丢弃 ${data.dropped} 条 · 写入失败 ${data.write_failures} 次${cumulative} · 更新于 ${new Date().toLocaleTimeString('zh-CN')}`;
  }
  let generation = 0, inflight;
  async function load() {
    const current = ++generation;
    inflight?.abort();
    refresh.disabled = true;
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 8000);
    inflight = controller;
    try {
      const response = await fetch(`/my-schedule/admin/performance?hours=${range.value}&channel=${channel.value}`, {
        credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
      });
      if (response.status === 401 || response.status === 403 || response.redirected)
        throw new Error('管理员会话已失效，请重新登录');
      if (!response.ok) throw new Error('暂时无法读取性能数据，请稍后刷新');
      const data = await response.json();
      if (current === generation) render(data);
    } catch (error) { if (current === generation) status.textContent = error.name === 'AbortError' ? '读取超时，可点击刷新重试。' : error.message; }
    finally { clearTimeout(timeout); if (current === generation) refresh.disabled = false; }
  }
  range.addEventListener('change', load);
  channel.addEventListener('change', load);
  refresh.addEventListener('click', load);
  setInterval(() => { if (!document.hidden && panel.isConnected) load(); }, 60000);
  load();
})();
