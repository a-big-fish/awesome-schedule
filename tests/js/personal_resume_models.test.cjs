const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('app/static/js/personal_resume_models.js', 'utf8');

function el(tag = 'div') {
  const node = {
    tagName: tag.toUpperCase(), textContent: '', className: '', type: '', disabled: false,
    hidden: false, children: [], dataset: {}, attributes: {}, listeners: {}, open: false,
    append(...items) {node.children.push(...items);},
    replaceChildren(...items) {node.children = items;},
    setAttribute(name, value) {node.attributes[name] = value;},
    addEventListener(name, callback) {node.listeners[name] = callback;},
    toggleAttribute(name, value) {node.attributes[name] = value;},
    showModal() {node.open = true;}, close() {node.open = false;}, focus() {},
  };
  return node;
}

function browser(initialModels) {
  const calls = [];
  let models = initialModels.slice();
  const root = el();
  const list = el('ul');
  const empty = el('p');
  const add = el('button');
  const dialog = el('dialog');
  const form = el('form');
  const discover = el('button');
  const status = el('p');
  const closeButton = el('button');
  const select = el('select');
  const save = el('button');
  form.elements = {base_url: el('input'), api_key: el('input'), model: select, name: el('input')};
  form.reset = () => {form.elements.base_url.value = ''; form.elements.api_key.value = ''; form.elements.name.value = '';};
  form.querySelector = selector => selector === '[type=submit]' ? save : null;
  const parts = {
    '[data-resume-models-list]': list, '[data-resume-models-empty]': empty, '[data-resume-models-add]': add,
    '[data-resume-models-dialog]': dialog, '[data-resume-models-form]': form,
    '[data-resume-models-discover]': discover, '[data-resume-models-status]': status,
    '[data-resume-models-close]': closeButton,
  };
  root.querySelector = selector => parts[selector];
  const document = {
    querySelector: selector => selector === '[data-resume-models]' ? root
      : selector === 'meta[name="calendar-csrf"]' ? {content: 'csrf'} : null,
    createElement: tag => el(tag),
  };
  const fetch = async (url, options = {}) => {
    calls.push({url, method: options.method || 'GET', body: options.body && JSON.parse(options.body)});
    if (url.endsWith('/discover')) return {ok: true, status: 200, json: async () => ({models: ['alpha', 'beta']})};
    if (options.method === 'DELETE') {models = models.filter(item => !url.endsWith(item.id)); return {ok: true, status: 204, json: async () => null};}
    if (options.method === 'POST') {
      const payload = JSON.parse(options.body);
      return {ok: true, status: 201, json: async () => ({id: 'new-id', name: payload.name || payload.model, model: payload.model})};
    }
    return {ok: true, status: 200, json: async () => ({models})};
  };
  const context = {
    document, fetch, console, JSON, Promise, Error, encodeURIComponent,
    window: {confirm: () => true, focus() {}},
    Option: class {constructor(text, value) {this.text = text; this.value = value;}},
  };
  vm.runInNewContext(source, context);
  return {calls, list, empty, add, dialog, form, discover, status, select, save, closeButton};
}

const tick = () => new Promise(resolve => setImmediate(resolve));

test('settings lists configured models and removes a custom one', async () => {
  const app = browser([
    {id: 'default', name: 'DS', model: 'deepseek-v4.1-flash'},
    {id: 'custom-1', name: '我的网关', model: 'gateway-model'},
  ]);
  await tick();
  assert.equal(app.list.children.length, 2);
  assert.equal(app.empty.hidden, true);
  const custom = app.list.children[1];
  const remove = custom.children.at(-1);
  await remove.listeners.click();
  await tick();
  assert.equal(app.calls.at(-1).method, 'DELETE');
  assert.equal(app.list.children.length, 1);
  assert.equal(app.list.children[0].children.at(-1).textContent, '默认模型');
});

test('adding a model discovers IDs first and then saves the chosen one', async () => {
  const app = browser([{id: 'default', name: 'DS', model: 'deepseek-v4.1-flash'}]);
  await tick();
  app.add.listeners.click();
  assert.equal(app.dialog.open, true);
  app.form.elements.base_url.value = 'https://api.example.com/v1';
  app.form.elements.api_key.value = 'secret';
  await app.discover.listeners.click();
  await tick();
  assert.equal(app.select.disabled, false);
  assert.equal(app.select.children.length, 3);
  app.select.value = 'alpha';
  app.select.listeners.change();
  assert.equal(app.save.disabled, false);
  await app.form.listeners.submit({preventDefault() {}});
  await tick();
  assert.equal(app.calls.at(-1).method, 'POST');
  assert.equal(app.calls.at(-1).body.model, 'alpha');
  assert.equal(app.dialog.open, false);
  assert.equal(app.list.children.length, 2);
});

test('a missing API URL blocks discovery with a clear message', async () => {
  const app = browser([{id: 'default', name: 'DS', model: 'deepseek-v4.1-flash'}]);
  await tick();
  app.add.listeners.click();
  await app.discover.listeners.click();
  assert.match(app.status.textContent, /API URL/);
  assert.equal(app.calls.filter(call => call.url.endsWith('/discover')).length, 0);
});
