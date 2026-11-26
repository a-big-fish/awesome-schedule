(() => {
  const categories = {stack: '技术栈与岗位', project: '项目与实习', evidence: '数据与证据',
    structure: '篇幅与结构', wording: '措辞'};
  let dispose = () => {};
  const element = (tag, text) => {
    const node = document.createElement(tag);
    node.textContent = text;
    return node;
  };

  async function readEvents(response, receive) {
    if (!response.ok) {
      const error = await response.json().catch(() => ({}));
      throw Error(typeof error.detail === 'string' ? error.detail : '请求失败，请稍后重试。');
    }
    if (!response.headers.get('Content-Type')?.includes('text/event-stream') || !response.body) {
      throw Error('无法接收 AI 回复，请检查网络或登录状态。');
    }
    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = '';
    let complete = false;
    try {
      while (!complete) {
        const {done, value} = await reader.read();
        if (done) break;
        buffer += value;
        const frames = buffer.split('\n\n');
        buffer = frames.pop();
        for (const frame of frames) {
          if (!frame.startsWith('data: ')) continue;
          const event = JSON.parse(frame.slice(6));
          if (event.type === 'error') throw Error(event.message);
          if (event.type === 'done') { complete = true; break; }
          receive(event);
        }
      }
      if (!complete) throw Error('连接已中断，请重试。');
    } finally {
      void reader.cancel().catch(() => {});
    }
  }

  const assistantUrl = '/my-schedule/resume/assistant';
  const csrfHeader = () => ({'X-CSRF-Token': document.querySelector('meta[name="calendar-csrf"]')?.content || ''});

  async function modelRequest(path, signal, method = 'GET', payload) {
    const headers = {Accept: 'application/json', ...csrfHeader()};
    if (payload) headers['Content-Type'] = 'application/json';
    const response = await fetch(assistantUrl + '/models' + path, {
      method, cache: 'no-store', credentials: 'same-origin', headers, signal,
      body: payload && JSON.stringify(payload),
    });
    if (response.status === 204) return;
    const data = await response.json().catch(() => null);
    if (!response.ok || !data) {
      throw Error(typeof data?.detail === 'string' ? data.detail : '无法加载模型配置，请检查网络或登录状态。');
    }
    return data;
  }

  function initModels(find, onSelection) {
    const picker = find('[data-resume-model-picker]');
    const choices = find('[data-resume-model-buttons]');
    const add = find('[data-resume-model-add]');
    const dialog = find('[data-resume-model-dialog]');
    const form = find('[data-resume-model-form]');
    const discover = find('[data-resume-model-discover]');
    const save = form.querySelector('[type=submit]');
    const close = find('[data-resume-model-close]');
    const status = find('[data-resume-model-status]');
    const selectionStatus = find('[data-resume-model-selection-status]');
    const management = find('[data-resume-model-management]');
    const saved = find('[data-resume-model-saved]');
    const lifecycle = new AbortController();
    const {base_url: baseUrl, api_key: key, model, name} = form.elements;
    let models = [], selected = [], loaded = false, loading = false, pending = false;

    function message(text, isError = false, target = status) {
      target.textContent = text;
      target.toggleAttribute('data-error', isError);
    }

    function selectionChanged() {
      for (const button of choices.children) {
        button.setAttribute('aria-pressed', String(selected.includes(button.dataset.modelId)));
      }
      onSelection();
    }

    function render() {
      choices.replaceChildren();
      saved.replaceChildren();
      for (const entry of models) {
        const button = element('button', entry.name);
        button.type = 'button';
        button.className = 'btn secondary small';
        button.title = entry.model;
        button.dataset.modelId = entry.id;
        button.addEventListener('click', () => {
          if (selected.includes(entry.id)) {
            selected = selected.filter(id => id !== entry.id);
          } else if (selected.length >= 2) {
            message('每次最多选择两个模型。请先取消选中一个。', true, selectionStatus);
            return;
          } else {
            selected.push(entry.id);
          }
          message(selected.length ? '' : '请选择至少一个模型。', false, selectionStatus);
          selectionChanged();
        });
        choices.append(button);

        if (entry.id === 'default') continue;
        const item = element('li', '');
        const remove = element('button', '移除');
        remove.type = 'button';
        remove.className = 'btn secondary small';
        remove.setAttribute('aria-label', '移除模型 ' + entry.name);
        remove.addEventListener('click', async () => {
          if (!confirm('移除模型「' + entry.name + '」？')) return;
          remove.disabled = true;
          try {
            await modelRequest('/' + encodeURIComponent(entry.id), lifecycle.signal, 'DELETE');
            models = models.filter(item => item.id !== entry.id);
            selected = selected.filter(id => id !== entry.id);
            render();
            close.focus();
          } catch (error) {
            if (!lifecycle.signal.aborted) message(error.message, true);
          } finally {
            remove.disabled = false;
          }
        });
        item.append(element('span', entry.name), remove);
        saved.append(item);
      }
      management.hidden = !saved.childElementCount;
      selectionChanged();
    }

    async function load() {
      if (loaded || loading) return;
      loading = true;
      add.disabled = true;
      message('正在加载模型配置…', false, selectionStatus);
      try {
        ({models} = await modelRequest('', lifecycle.signal));
        selected = models.length ? [models[0].id] : [];
        loaded = true;
        render();
        message(models.length ? '' : '请点击「+」添加一个模型。', false, selectionStatus);
      } catch (error) {
        if (!lifecycle.signal.aborted) message(error.message, true, selectionStatus);
      } finally {
        loading = false;
        add.disabled = false;
      }
    }

    function resetModelList() {
      model.replaceChildren(new Option('请先获取可用模型', ''));
      model.disabled = true;
      save.disabled = true;
      message('');
    }

    function setPending(value) {
      pending = value;
      for (const input of [baseUrl, key, name, discover, close]) input.disabled = value;
      form.setAttribute('aria-busy', String(value));
    }

    add.addEventListener('click', () => {
      form.reset();
      resetModelList();
      dialog.showModal();
    });
    close.addEventListener('click', () => dialog.close());
    dialog.addEventListener('cancel', event => { if (pending) event.preventDefault(); });
    dialog.addEventListener('close', () => {
      form.reset();
      resetModelList();
      add.focus();
    });
    for (const input of [baseUrl, key]) input.addEventListener('input', resetModelList);
    model.addEventListener('change', () => { save.disabled = pending || !model.value; });

    discover.addEventListener('click', async () => {
      if (pending || !baseUrl.reportValidity() || !key.reportValidity()) return;
      resetModelList();
      const credentials = {base_url: baseUrl.value.trim(), api_key: key.value.trim()};
      setPending(true);
      message('正在获取模型列表…');
      try {
        const data = await modelRequest('/discover', lifecycle.signal, 'POST', credentials);
        if (!data.models.length) {
          message('这个接口没有返回可用模型。', true);
          return;
        }
        model.replaceChildren(new Option('请选择模型', ''), ...data.models.map(id => new Option(id, id)));
        model.disabled = false;
        message(`已获取 ${data.models.length} 个模型，请选择一个。`);
        model.focus();
      } catch (error) {
        if (!lifecycle.signal.aborted) message(error.message, true);
      } finally {
        setPending(false);
      }
    });

    form.addEventListener('submit', async event => {
      event.preventDefault();
      if (pending || !model.value) return;
      const payload = {name: name.value.trim(), base_url: baseUrl.value.trim(),
        api_key: key.value.trim(), model: model.value};
      setPending(true);
      save.disabled = true;
      model.disabled = true;
      message('正在保存模型…');
      try {
        const entry = await modelRequest('', lifecycle.signal, 'POST', payload);
        models.push(entry);
        const didSelect = selected.length < 2;
        if (didSelect) selected.push(entry.id);
        render();
        message(didSelect ? '' : '模型已保存。取消选中一个模型后即可切换。', false, selectionStatus);
        dialog.close();
      } catch (error) {
        if (!lifecycle.signal.aborted) message(error.message, true);
      } finally {
        setPending(false);
        model.disabled = !dialog.open;
        save.disabled = !dialog.open || !model.value;
      }
    });

    return {
      show: visible => {
        picker.hidden = !visible;
        selectionStatus.hidden = !visible;
        if (visible) void load();
      },
      selected: () => selected.map(id => models.find(model => model.id === id)),
      clearNotice: () => message('', false, selectionStatus),
      dispose: () => {
        lifecycle.abort();
        if (dialog.open) dialog.close();
      },
    };
  }

  function init() {
    dispose();
    const panel = document.querySelector('[data-resume-assistant]');
    if (!panel) return;
    const resume = document.querySelector('[data-resume-cache]');
    const toggle = resume.querySelector('[data-resume-assistant-toggle]');
    const plan = document.querySelector('[data-interview-plan]');
    const find = selector => panel.querySelector(selector);
    const heading = find('h2');
    const status = find('[data-assistant-notice]');
    const modeButtons = [...panel.querySelectorAll('[data-assistant-mode]')];
    const setup = find('[data-practice-setup]');
    const session = find('[data-practice-session]');
    const transcript = find('[data-practice-turns]');
    const answerForm = find('[data-practice-answer]');
    const answerInput = answerForm.elements.answer;
    const resetButton = find('[data-practice-reset]');
    const reviewButton = find('[data-practice-review]');
    const sourceBox = find('[data-practice-sources]');
    const optimizeForm = find('[data-optimize-form]');
    const results = find('[data-optimize-results]');
    const resultTemplate = find('[data-optimize-result-template]');
    const notices = {practice: '', optimize: ''};
    let mode = 'practice', version = resume.dataset.resumeVersion || '', request = null;
    let options = {}, history = [], ended = false;
    let error = {practice: false, optimize: false};
    const modelPicker = initModels(find, controls);

    function showStatus(text, isError = false, target = mode) {
      notices[target] = text;
      error[target] = isError;
      if (target !== mode) return;
      status.textContent = text;
      status.toggleAttribute('data-error', isError);
    }

    function switchMode(next) {
      mode = next;
      modeButtons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.assistantMode === mode)));
      panel.querySelectorAll('[data-assistant-pane]').forEach(pane => {pane.hidden = pane.dataset.assistantPane !== mode;});
      modelPicker.show(mode === 'optimize');
      showStatus(notices[mode], error[mode]);
    }

    function showPanel(open, focus = true) {
      panel.hidden = !open;
      plan.hidden = open;
      toggle.setAttribute('aria-expanded', String(open));
      toggle.textContent = open ? '面试计划' : '简历助手';
      if (focus) (open ? heading : toggle).focus();
    }

    function controls() {
      const busy = Boolean(request);
      setup.hidden = history.length > 0;
      session.hidden = history.length === 0 && !busy;
      answerForm.hidden = ended || history.length === 0;
      setup.querySelector('button').disabled = busy || !version;
      optimizeForm.querySelector('button').disabled = busy || !version || !modelPicker.selected().length;
      answerForm.querySelector('[type=submit]').disabled = busy || !version;
      answerInput.disabled = busy;
      resetButton.disabled = busy;
      reviewButton.disabled = busy || !history.some(turn => turn.role === 'user');
    }

    function resetPractice() {
      history = [];
      ended = false;
      transcript.replaceChildren();
      sourceBox.replaceChildren();
      sourceBox.hidden = true;
      answerInput.value = '';
      showStatus('', false, 'practice');
      controls();
    }

    function syncVersion() {
      const next = resume.dataset.resumeVersion || '';
      if (version === next) return;
      request?.abort();
      request = null;
      version = next;
      resetPractice();
      results.hidden = true;
      results.replaceChildren();
      const message = version ? '简历已更新，可重新开始练习或分析。' : '请先上传或同步 PDF 简历。';
      showStatus(message, false, 'practice');
      showStatus(message, false, 'optimize');
    }

    function addTurn(role, label, text = '') {
      const turn = document.createElement('li');
      turn.dataset.role = role;
      const body = element('p', text);
      turn.append(element('strong', label), body);
      transcript.append(turn);
      transcript.scrollTop = transcript.scrollHeight;
      return {turn, body};
    }

    function showSources(sources) {
      sourceBox.replaceChildren();
      sourceBox.hidden = !options.reference_experiences;
      if (sourceBox.hidden) return;
      sourceBox.append(sources.length ? '参考面经 · ' : '未找到相关的个人面经，本轮根据简历提问。');
      for (const source of sources) {
        const link = element('a', source.title || '查看面经');
        link.href = '/my-schedule/experiences/' + encodeURIComponent(source.id);
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        sourceBox.append(link);
      }
    }

    function appendSuggestion(resultPanel, suggestion) {
      const items = resultPanel.querySelector('[data-optimize-items]');
      const item = document.createElement('li');
      item.append(element('h4', categories[suggestion.category]));
      if (suggestion.page) {
        const link = element('a', `查看 PDF 第 ${suggestion.page} 页`);
        const url = new URL(resume.querySelector('[data-resume-open]').href);
        url.hash = `page=${suggestion.page}`;
        link.href = url.href;
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        item.append(link, element('blockquote', suggestion.original));
      }
      item.append(element('p', suggestion.issue));
      if (suggestion.rewrite) {
        item.append(element('p', '建议改写 · ' + suggestion.rewrite));
        const button = element('button', '复制改写');
        button.type = 'button';
        button.className = 'btn secondary small';
        button.addEventListener('click', async () => {
          try {
            await navigator.clipboard.writeText(suggestion.rewrite);
            showStatus('已复制改写。', false, 'optimize');
          } catch {
            showStatus('无法访问剪贴板，可以选中文字后手动复制。', true, 'optimize');
          }
        });
        item.append(button);
      }
      items.append(item);
    }

    function updateModelSummary(card, pageCount) {
      const count = card.querySelector('[data-optimize-items]').childElementCount;
      const summary = card.querySelector('[data-optimize-summary]');
      summary.textContent = pageCount === undefined
        ? `正在分析 · 已收到 ${count} 条建议`
        : `当前简历 ${pageCount} 页 · ${count} 条建议`;
      card.querySelector('[data-optimize-empty]').hidden = pageCount === undefined || count > 0;
    }

    function createResultPanels(models) {
      const panels = new Map();
      results.replaceChildren();
      for (const model of models) {
        const card = resultTemplate.content.firstElementChild.cloneNode(true);
        const title = card.querySelector('[data-optimize-model-name]');
        title.textContent = model.name;
        title.title = model.model;
        card.querySelector('[data-optimize-scroll]').setAttribute('aria-label', model.name + '的简历建议');
        results.append(card);
        panels.set(model.id, card);
      }
      results.toggleAttribute('data-comparison', models.length === 2);
      results.hidden = false;
      return panels;
    }

    function showModelError(card, message) {
      const summary = card.querySelector('[data-optimize-summary]');
      const count = card.querySelector('[data-optimize-items]').childElementCount;
      summary.textContent = count ? `已保留 ${count} 条建议。 ${message}` : message;
      summary.setAttribute('data-error', '');
    }

    async function run(action, input = {}, models = []) {
      if (request) return;
      const target = action === 'optimize' ? 'optimize' : 'practice';
      if (!version || navigator.onLine === false) {
        showStatus(version ? '请联网后重试。' : '请先上传或同步 PDF 简历。', true, target);
        return;
      }
      const pending = new AbortController();
      request = pending;
      const timeout = setTimeout(() => pending.abort(), target === 'optimize' ? 135000 : 90000);
      const modelPanels = models.length ? createResultPanels(models) : new Map();
      const waiting = new Set(modelPanels.keys());
      const user = input.answer ? addTurn('user', '你', input.answer) : null;
      const ai = target === 'practice' ? addTurn('assistant', action === 'review' ? '练习复盘' : '面试教练') : null;
      let responseText = '', sources;
      controls();
      showStatus(target === 'practice' ? '正在生成回复…' : '', false, target);
      try {
        const response = await fetch(assistantUrl, {
          method: 'POST', cache: 'no-store', credentials: 'same-origin', signal: pending.signal,
          headers: {'Content-Type': 'application/json', Accept: 'text/event-stream',
            ...csrfHeader()},
          body: JSON.stringify({version, action, ...input}),
        });
        await readEvents(response, event => {
          if (request !== pending) return;
          if (event.type === 'delta' && ai) {
            responseText += event.content;
            ai.body.textContent = responseText;
            transcript.scrollTop = transcript.scrollHeight;
          }
          if (event.type === 'sources') sources = event.sources;
          if (waiting.has(event.model_id)) {
            const card = modelPanels.get(event.model_id);
            switch (event.type) {
              case 'model_progress':
                card.querySelector('[data-optimize-summary]').textContent =
                  event.phase === 'reasoning' ? '模型正在推理…' : '正在生成建议…';
                break;
              case 'model_suggestion':
                appendSuggestion(card, event.item);
                updateModelSummary(card);
                break;
              case 'model_done':
                updateModelSummary(card, event.page_count);
                waiting.delete(event.model_id);
                break;
              case 'model_error':
                showModelError(card, event.message);
                waiting.delete(event.model_id);
                break;
            }
          }
        });
        if (request !== pending) return;
        if (target === 'practice') {
          if (input.answer) {
            history.push({role: 'user', content: input.answer});
            answerInput.value = '';
          }
          history.push({role: 'assistant', content: responseText});
          if (sources) showSources(sources);
          ended = action === 'review';
        }
        if (waiting.size) throw Error('有模型未返回分析结果，请重试。');
        showStatus(ended && target === 'practice' ? '本轮练习已结束。' : '', false, target);
      } catch (error) {
        if (request !== pending) return;
        user?.turn.remove();
        ai?.turn.remove();
        const message = error.name === 'AbortError' ? '等待 AI 回复超时，请重试。' : error.message;
        for (const id of waiting) showModelError(modelPanels.get(id), message);
        if (!modelPanels.size) showStatus(message, true, target);
        else showStatus('', false, target);
      } finally {
        clearTimeout(timeout);
        if (request === pending) {
          request = null;
          controls();
          if (target === 'practice' && mode === target && !panel.hidden && !answerForm.hidden) answerInput.focus();
        }
      }
    }

    const recentHistory = () => history.slice(-12).map(turn => ({...turn, content: turn.content.slice(0, 3000)}));
    toggle.hidden = false;
    toggle.addEventListener('click', () => showPanel(panel.hidden));
    find('[data-assistant-close]').addEventListener('click', () => showPanel(false));
    modeButtons.forEach(button => button.addEventListener('click', () => switchMode(button.dataset.assistantMode)));
    resetButton.addEventListener('click', () => {
      resetPractice();
      setup.elements.position.focus();
    });
    setup.addEventListener('submit', event => {
      event.preventDefault();
      options = {position: setup.elements.position.value.trim(), focus: setup.elements.focus.value.trim(),
        reference_experiences: setup.elements.reference_experiences.checked};
      void run('start', options);
    });
    answerForm.addEventListener('submit', event => {
      event.preventDefault();
      const answer = answerInput.value.trim();
      if (answer) void run('answer', {...options, answer, history: recentHistory()});
    });
    reviewButton.addEventListener('click', () => void run('review', {...options, history: recentHistory()}));
    optimizeForm.addEventListener('submit', event => {
      event.preventDefault();
      const models = modelPicker.selected();
      if (!models.length) return;
      modelPicker.clearNotice();
      void run('optimize', {
        position: optimizeForm.elements.position.value.trim(),
        model_ids: models.map(model => model.id),
        stream_suggestions: true,
      }, models);
    });
    const observer = new MutationObserver(syncVersion);
    observer.observe(resume, {attributeFilter: ['data-resume-version']});
    if (!version) {
      showStatus('请先上传或同步 PDF 简历。', false, 'practice');
      showStatus('请先上传或同步 PDF 简历。', false, 'optimize');
    }
    controls();
    dispose = () => {
      observer.disconnect();
      modelPicker.dispose();
      const pending = request;
      request = null;
      pending?.abort();
    };
  }

  document.addEventListener('personal:navigated', init);
  addEventListener('pagehide', () => dispose());
  init();
})();
