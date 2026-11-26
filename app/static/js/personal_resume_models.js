/* Manage the account's resume-review API models from 个人设置. */
(() => {
  const root = document.querySelector('[data-resume-models]');
  if (!root) return;
  const base = '/my-schedule/resume/assistant/models';
  const list = root.querySelector('[data-resume-models-list]');
  const empty = root.querySelector('[data-resume-models-empty]');
  const add = root.querySelector('[data-resume-models-add]');
  const dialog = root.querySelector('[data-resume-models-dialog]');
  const form = root.querySelector('[data-resume-models-form]');
  const discover = root.querySelector('[data-resume-models-discover]');
  const status = root.querySelector('[data-resume-models-status]');
  const close = root.querySelector('[data-resume-models-close]');
  const save = form.querySelector('[type=submit]');
  const {base_url: baseUrl, api_key: key, model, name} = form.elements;
  const csrf = () => document.querySelector('meta[name="calendar-csrf"]')?.content || '';
  let models = [], pending = false;

  function message(text, isError = false) {
    status.textContent = text;
    status.toggleAttribute('data-error', isError);
  }

  async function request(path = '', method = 'GET', payload) {
    const headers = {Accept: 'application/json', 'X-CSRF-Token': csrf()};
    if (payload) headers['Content-Type'] = 'application/json';
    const response = await fetch(base + path, {
      method, cache: 'no-store', credentials: 'same-origin', headers,
      body: payload && JSON.stringify(payload),
    });
    if (response.status === 204) return;
    const data = await response.json().catch(() => null);
    if (!response.ok || !data) {
      throw Error(typeof data?.detail === 'string' ? data.detail : '无法加载模型配置，请检查网络或登录状态。');
    }
    return data;
  }

  function row(entry) {
    const item = document.createElement('li');
    const text = document.createElement('span');
    text.textContent = `${entry.name} · ${entry.model}`;
    item.append(text);
    if (entry.id === 'default') {
      const tag = document.createElement('small');
      tag.textContent = '默认模型';
      item.append(tag);
      return item;
    }
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'btn secondary small';
    remove.textContent = '移除';
    remove.setAttribute('aria-label', `移除模型 ${entry.name}`);
    remove.addEventListener('click', async () => {
      if (!window.confirm(`移除模型「${entry.name}」？`)) return;
      remove.disabled = true;
      try {
        await request('/' + encodeURIComponent(entry.id), 'DELETE');
        models = models.filter(item => item.id !== entry.id);
        render();
        message('模型已移除。');
      } catch (error) {
        message(error.message, true);
        remove.disabled = false;
      }
    });
    item.append(remove);
    return item;
  }

  function render() {
    list.replaceChildren(...models.map(row));
    empty.hidden = models.length > 0;
  }

  async function load() {
    message('正在加载模型配置…');
    try {
      ({models} = await request());
      render();
      message(models.length > 1 ? '' : '未添加自定义模型时，使用 .env 中的默认模型。');
    } catch (error) {
      message(error.message, true);
    }
  }

  function reset() {
    form.reset();
    model.replaceChildren(new Option('请先获取可用模型', ''));
    model.disabled = true;
    save.disabled = true;
  }

  function setPending(value) {
    pending = value;
    for (const input of [baseUrl, key, name, discover, close, save]) input.disabled = value;
    form.setAttribute('aria-busy', String(value));
  }

  add.addEventListener('click', () => {
    reset();
    message('');
    dialog.showModal();
  });
  close.addEventListener('click', () => dialog.close());
  dialog.addEventListener('cancel', event => { if (pending) event.preventDefault(); });
  dialog.addEventListener('close', () => { if (!pending) add.focus(); });

  discover.addEventListener('click', async () => {
    const credentials = {base_url: baseUrl.value.trim(), api_key: key.value};
    if (!credentials.base_url || !credentials.api_key) {
      message('请先填写 API URL 和 API Key，再获取模型列表。', true);
      return;
    }
    discover.disabled = true;
    message('正在获取可用模型…');
    try {
      const data = await request('/discover', 'POST', credentials);
      if (!data.models.length) {
        message('该接口没有返回可用模型，请检查 API URL 和权限。', true);
        return;
      }
      model.replaceChildren(new Option('请选择模型', ''), ...data.models.map(id => new Option(id, id)));
      model.disabled = false;
      save.disabled = true;
      message(`已获取 ${data.models.length} 个模型，请选择一个。`);
    } catch (error) {
      message(error.message, true);
    } finally {
      discover.disabled = false;
    }
  });

  model.addEventListener('change', () => { save.disabled = !model.value; });

  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (pending || !model.value) return;
    setPending(true);
    message('正在保存模型…');
    try {
      const entry = await request('', 'POST', {
        name: name.value.trim(), base_url: baseUrl.value.trim(), api_key: key.value, model: model.value,
      });
      models.push(entry);
      render();
      dialog.close();
      message('模型已保存，可在简历助手中选择。');
    } catch (error) {
      message(error.message, true);
    } finally {
      setPending(false);
    }
  });

  load();
})();
