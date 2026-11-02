(() => {
function initTimePicker() {
  const eventType = document.querySelector('[data-event-type]');
  const positionField = document.querySelector('[data-position-field]');
  const invitationForm = document.querySelector('[data-invitation-form]');
  const invitationDivider = document.querySelector('[data-invitation-divider]');
  if (eventType && !eventType.dataset.timePickerReady) {
    eventType.dataset.timePickerReady = '1';
    const updateEventFields = () => {
      const isInterview = eventType.value === 'interview';
      if (positionField) {
        positionField.hidden = !isInterview;
        positionField.querySelector('input').disabled = !isInterview;
      }
      if (invitationForm) invitationForm.hidden = !isInterview;
      if (invitationDivider) invitationDivider.hidden = !isInterview;
    };
    eventType.addEventListener('change', updateEventFields);
    updateEventFields();
  }

  const dialog = document.querySelector('[data-time-dialog]');
  document.querySelectorAll('input[type="datetime-local"]').forEach(input => {
    const field = document.createElement('div');
    field.className = 'personal-time-field';
    const label = document.createElement('span');
    label.hidden = true;
    label.textContent = input.closest('label')?.childNodes[0]?.textContent.trim() || '日期和时间';
    const trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'input personal-time-trigger';
    trigger.dataset.timeTrigger = '';
    trigger.setAttribute('aria-label', `选择${label.textContent}`);
    trigger.setAttribute('aria-haspopup', 'dialog');
    input.before(field);
    input.dataset.timeValue = '';
    input.dataset.timeRequired = input.required ? '1' : '';
    input.type = 'hidden';
    field.append(label, input, trigger);
  });
  const fields = [...document.querySelectorAll('.personal-time-field')];
  fields.forEach(field => {
    if (field.dataset.timeBound) return;
    field.dataset.timeBound = '1';
    const input = field.querySelector('[data-time-value]');
    const trigger = field.querySelector('[data-time-trigger]');
    trigger.textContent = input.value ? input.value.replace('T', ' ') : '请选择日期和时间';
    input.addEventListener('change', () => { trigger.textContent = input.value ? input.value.replace('T', ' ') : '请选择日期和时间'; });
    trigger.addEventListener('click', () => dialog.dispatchEvent(new CustomEvent('time:open', {detail: field})));
  });
  if (!dialog || !fields.length || dialog.dataset.timePickerReady) return;
  dialog.dataset.timePickerReady = '1';

  const heading = dialog.querySelector('[data-time-heading]');
  const progress = dialog.querySelector('[data-time-progress]');
  const options = dialog.querySelector('[data-time-options]');
  const back = dialog.querySelector('[data-time-back]');
  let activeField = null;
  let step = 0;
  let selectedDate = '';
  let selectedHour = '';
  let selectedMinute = '';
  let onlyDate = false;
  let viewYear = 0;
  let viewMonth = 0;
  let calendarMode = 'days';
  let yearPageStart = 0;

  const pad = number => String(number).padStart(2, '0');
  const validValue = value => /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value);
  const formatValue = value => validValue(value)
    ? `${value.slice(0, 4)}/${value.slice(5, 7)}/${value.slice(8, 10)} ${value.slice(11)}`
    : value || '请选择日期和时间';
  function updateEndFromStart(startValue) {
    const endField = activeField.closest('form')?.querySelector('[name="end_at"], [name="reviewed_end"]')?.closest('.personal-time-field');
    if (!validValue(startValue) || !endField) return;
    const year = Number(startValue.slice(0, 4));
    const month = Number(startValue.slice(5, 7)) - 1;
    const day = Number(startValue.slice(8, 10));
    const hour = Number(startValue.slice(11, 13));
    const minute = Number(startValue.slice(14, 16));
    const end = new Date(Date.UTC(year, month, day, hour + 1, minute));
    const endValue = `${end.getUTCFullYear()}-${pad(end.getUTCMonth() + 1)}-${pad(end.getUTCDate())}T${pad(end.getUTCHours())}:${pad(end.getUTCMinutes())}`;
    endField.querySelector('[data-time-value]').value = endValue;
    endField.querySelector('[data-time-trigger]').textContent = formatValue(endValue);
  }

  dialog.addEventListener('time:open', event => {
      activeField = event.detail;
      const value = activeField.querySelector('[data-time-value]');
      step = 0;
      onlyDate = activeField.dataset.timeMode === 'date';
      selectedDate = validValue(value.value) ? value.value.slice(0, 10) : '';
      selectedHour = validValue(value.value) ? value.value.slice(11, 13) : '';
      selectedMinute = validValue(value.value) ? value.value.slice(14, 16) : '';
      const today = new Date();
      const shownDate = selectedDate || `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;
      viewYear = Number(shownDate.slice(0, 4));
      viewMonth = Number(shownDate.slice(5, 7));
      calendarMode = 'days';
      render();
      dialog.showModal();
  });

  function calendarButton(label, className, onClick, ariaLabel = label) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = className;
    button.textContent = label;
    button.setAttribute('aria-label', ariaLabel);
    button.addEventListener('click', onClick);
    return button;
  }

  function workTime(end = false) {
    return document.querySelector(`meta[name="workday-${end ? 'end' : 'start'}"]`)?.content || (end ? '18:00' : '08:00');
  }

  function commitSelection(dateOnly) {
    const value = activeField.querySelector('[data-time-value]');
    const isEnd = ['end_at', 'reviewed_end'].includes(value.name);
    const next = `${selectedDate}T${dateOnly ? workTime(isEnd) : selectedHour + ':' + selectedMinute}`;
    if ((value.min && next < value.min) || (value.max && next > value.max)) {
      let error = options.querySelector('[data-time-error]');
      if (!error) {
        error = document.createElement('p');
        error.dataset.timeError = '';
        error.setAttribute('role', 'alert');
        options.append(error);
      }
      error.textContent = `请选择允许范围内的时间${value.min ? '（最早 ' + value.min.replace('T', ' ') + '）' : ''}`;
      return;
    }
    value.value = next;
    activeField.dataset.timeMode = dateOnly ? 'date' : 'time';
    activeField.querySelector('[data-time-trigger]').textContent = formatValue(next);
    if (['start_at', 'reviewed_start'].includes(value.name)) {
      const end = activeField.closest('form')?.querySelector('[name="end_at"], [name="reviewed_end"]');
      if (dateOnly && end && end.value.slice(0, 10) <= selectedDate) {
        end.value = `${selectedDate}T${workTime(true)}`;
        end.closest('.personal-time-field').dataset.timeMode = 'date';
        end.dispatchEvent(new Event('change', {bubbles: true}));
      } else if (!dateOnly) updateEndFromStart(next);
    }
    value.dispatchEvent(new Event('input', {bubbles: true}));
    value.dispatchEvent(new Event('change', {bubbles: true}));
    dialog.close();
    activeField.querySelector('[data-time-trigger]').focus();
  }

  function renderCalendar() {
    progress.textContent = onlyDate ? '选择日期 · 使用默认工作时间' : '选择日期 → 选择时间';
    options.replaceChildren();
    const calendar = document.createElement('div');
    calendar.className = 'personal-calendar-picker';
    const header = document.createElement('div');
    header.className = 'personal-calendar-header';

    if (calendarMode === 'years') {
      header.append(
        calendarButton('‹', 'personal-calendar-nav', () => { yearPageStart -= 12; renderCalendar(); }, '前 12 年'),
        Object.assign(document.createElement('strong'), { textContent: `${yearPageStart}—${yearPageStart + 11} 年` }),
        calendarButton('›', 'personal-calendar-nav', () => { yearPageStart += 12; renderCalendar(); }, '后 12 年')
      );
      const grid = document.createElement('div');
      grid.className = 'personal-calendar-pick-grid';
      for (let year = yearPageStart; year < yearPageStart + 12; year += 1) {
        const choice = calendarButton(String(year), 'personal-calendar-pick', () => {
          viewYear = year;
          calendarMode = 'months';
          renderCalendar();
        });
        if (year === viewYear) choice.classList.add('selected');
        grid.append(choice);
      }
      calendar.append(header, grid, calendarButton('返回日历', 'personal-calendar-return', () => {
        calendarMode = 'days';
        renderCalendar();
      }));
      options.append(calendar);
      return;
    }

    if (calendarMode === 'months') {
      header.append(
        calendarButton('‹', 'personal-calendar-nav', () => { viewYear -= 1; renderCalendar(); }, '上一年'),
        Object.assign(document.createElement('strong'), { textContent: `${viewYear} 年` }),
        calendarButton('›', 'personal-calendar-nav', () => { viewYear += 1; renderCalendar(); }, '下一年')
      );
      const grid = document.createElement('div');
      grid.className = 'personal-calendar-pick-grid';
      for (let month = 1; month <= 12; month += 1) {
        const choice = calendarButton(`${month} 月`, 'personal-calendar-pick', () => {
          viewMonth = month;
          calendarMode = 'days';
          renderCalendar();
        });
        if (month === viewMonth) choice.classList.add('selected');
        grid.append(choice);
      }
      calendar.append(header, grid, calendarButton('返回日历', 'personal-calendar-return', () => {
        calendarMode = 'days';
        renderCalendar();
      }));
      options.append(calendar);
      return;
    }

    const moveMonth = delta => {
      const target = new Date(viewYear, viewMonth - 1 + delta, 1);
      viewYear = target.getFullYear();
      viewMonth = target.getMonth() + 1;
      renderCalendar();
    };
    header.append(
      calendarButton('‹', 'personal-calendar-nav', () => moveMonth(-1), '上个月'),
      calendarButton(`${viewYear} 年`, 'personal-calendar-title', () => {
        yearPageStart = Math.floor(viewYear / 12) * 12;
        calendarMode = 'years';
        renderCalendar();
      }, '选择年份'),
      calendarButton(`${viewMonth} 月`, 'personal-calendar-title', () => {
        calendarMode = 'months';
        renderCalendar();
      }, '选择月份'),
      calendarButton('›', 'personal-calendar-nav', () => moveMonth(1), '下个月')
    );
    const grid = document.createElement('div');
    grid.className = 'personal-calendar-day-grid';
    for (const weekday of ['一', '二', '三', '四', '五', '六', '日']) {
      const label = document.createElement('span');
      label.className = 'personal-calendar-weekday';
      label.textContent = weekday;
      grid.append(label);
    }
    const firstWeekday = (new Date(viewYear, viewMonth - 1, 1).getDay() + 6) % 7;
    for (let index = 0; index < firstWeekday; index += 1) {
      const blank = document.createElement('span');
      blank.className = 'personal-calendar-blank';
      grid.append(blank);
    }
    const days = new Date(viewYear, viewMonth, 0).getDate();
    for (let day = 1; day <= days; day += 1) {
      const date = `${viewYear}-${pad(viewMonth)}-${pad(day)}`;
      const choice = calendarButton(String(day), 'personal-calendar-day', () => {
        selectedDate = date;
        if (onlyDate) commitSelection(true);
        else { step = 1; render(); }
      }, `${viewYear}年${viewMonth}月${day}日`);
      if (date === selectedDate) choice.classList.add('selected');
      grid.append(choice);
    }
    const modes = document.createElement('div');
    modes.className = 'personal-date-mode';
    for (const [dateOnly, label] of [[true, '只选日期'], [false, '指定时间']]) {
      const button = calendarButton(label, 'personal-date-mode-button', () => { onlyDate = dateOnly; renderCalendar(); });
      button.classList.toggle('selected', onlyDate === dateOnly);
      button.setAttribute('aria-pressed', String(onlyDate === dateOnly));
      modes.append(button);
    }
    const hint = document.createElement('p');
    hint.className = 'personal-date-hint';
    hint.textContent = onlyDate ? `使用默认工作时间 ${workTime()}–${workTime(true)}，可在个人设置中修改` : '先选择日期，再选择小时和分钟';
    const error = document.createElement('p');
    error.dataset.timeError = '';
    error.setAttribute('role', 'alert');
    calendar.append(header, grid);
    options.append(modes, hint, calendar, error);

    if (selectedDate.startsWith(`${viewYear}-${pad(viewMonth)}-`)) {
      const next = calendarButton(onlyDate ? '确认日期' : '下一步：选择时间', 'btn personal-time-next', () => {
        if (onlyDate) commitSelection(true);
        else { step = 1; render(); }
      });
      options.append(next);
    }
  }

  function render() {
    const labels = ['选择日期', '选择时间'];
    heading.textContent = `${activeField.querySelector('span').textContent} · ${labels[step]}`;
    progress.textContent = labels.map((label, index) => index === step ? `● ${label}` : label).join('  →  ');
    back.hidden = step === 0;
    dialog.classList.toggle('is-calendar', step === 0);
    options.replaceChildren();

    if (step === 0) {
      renderCalendar();
      return;
    }

    const columns = document.createElement('div');
    columns.className = 'personal-clock-columns';
    for (const [kind, count, title] of [['hour', 24, '小时'], ['minute', 60, '分钟']]) {
      const column = document.createElement('section');
      const caption = document.createElement('h3');
      caption.textContent = title;
      const grid = document.createElement('div');
      grid.className = 'personal-time-grid';
      grid.setAttribute('role', 'group');
      grid.setAttribute('aria-label', title);
      for (let index = 0; index < count; index += 1) {
        const number = pad(index);
        const choice = calendarButton(number, 'personal-time-choice', () => {
          if (kind === 'hour') selectedHour = number;
          else selectedMinute = number;
          grid.querySelectorAll('button').forEach(button => {
            const selected = button === choice;
            button.classList.toggle('selected', selected);
            button.setAttribute('aria-pressed', String(selected));
          });
          confirm.disabled = !selectedHour || !selectedMinute;
        }, `${number}${title === '小时' ? '时' : '分'}`);
        const selected = number === (kind === 'hour' ? selectedHour : selectedMinute);
        choice.classList.toggle('selected', selected);
        choice.setAttribute('aria-pressed', String(selected));
        grid.append(choice);
      }
      column.append(caption, grid);
      columns.append(column);
    }
    const error = document.createElement('p');
    error.dataset.timeError = '';
    error.setAttribute('role', 'alert');
    const confirm = calendarButton('确认时间', 'btn personal-time-next', () => commitSelection(false));
    confirm.disabled = !selectedHour || !selectedMinute;
    options.append(columns, error, confirm);
    requestAnimationFrame(() => {
      columns.querySelectorAll('.selected').forEach(choice => choice.scrollIntoView({block: 'nearest'}));
      (columns.querySelector('.personal-time-grid .selected') || columns.querySelector('button'))?.focus();
    });
  }

  dialog.querySelector('[data-time-now]').addEventListener('click', () => {
    const requestedZone = activeField.closest('form')?.querySelector('[name="timezone"]')?.value || 'Asia/Shanghai';
    const settings = {year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'};
    let formatter;
    try { formatter = new Intl.DateTimeFormat('en-GB', {...settings, timeZone: requestedZone}); }
    catch { formatter = new Intl.DateTimeFormat('en-GB', {...settings, timeZone: 'Asia/Shanghai'}); }
    const parts = Object.fromEntries(formatter.formatToParts(new Date()).map(part => [part.type, part.value]));
    selectedDate = `${parts.year}-${parts.month}-${parts.day}`;
    selectedHour = parts.hour;
    selectedMinute = parts.minute;
    commitSelection(false);
  });
  back.addEventListener('click', () => {
    if (step > 0) {
      step -= 1;
      render();
    }
  });
  dialog.querySelector('[data-time-close]').addEventListener('click', () => dialog.close());
  dialog.querySelector('[data-time-cancel]').addEventListener('click', () => dialog.close());
}
document.addEventListener('DOMContentLoaded', initTimePicker);
document.addEventListener('personal:navigated', initTimePicker);
if (typeof MutationObserver === 'function') new MutationObserver(records => {
  if (records.some(record => [...record.addedNodes].some(node => node.nodeType === 1 &&
    (node.matches?.('input[type="datetime-local"]') || node.querySelector?.('input[type="datetime-local"]'))))) initTimePicker();
}).observe(document.documentElement, {childList: true, subtree: true});
document.addEventListener('submit', event => {
  const missing = [...event.target.querySelectorAll('[data-time-required="1"]')].find(input => !input.value);
  if (missing) {
    event.preventDefault();
    missing.closest('.personal-time-field').querySelector('[data-time-trigger]').click();
  }
}, true);
})();
