const {test} = require('node:test');
const assert = require('node:assert/strict');
const {monthDays, shiftMonth} = require('../../app/static/js/personal_quick_calendar.js');

test('quick calendar has six full Monday-first weeks across year boundaries', () => {
  const days = monthDays('2027-01');
  assert.equal(days.length, 42);
  assert.equal(days[0], '2026-12-28');
  assert.equal(days.at(-1), '2027-02-07');
  assert.equal(new Set(days).size, 42);
});

test('quick calendar includes leap day and shifts months without day rollover', () => {
  assert.ok(monthDays('2028-02').includes('2028-02-29'));
  assert.ok(!monthDays('2027-02').includes('2027-02-29'));
  assert.equal(shiftMonth('2026-12-31', 1), '2027-01');
  assert.equal(shiftMonth('2027-01-31', -1), '2026-12');
});
