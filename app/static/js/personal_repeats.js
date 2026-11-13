(() => {
  function init() {
    document.querySelectorAll('[data-repeat-enabled]').forEach(toggle => {
      if (toggle.dataset.bound) return;
      toggle.dataset.bound = '1';
      const fieldset = toggle.closest('fieldset');
      const days = fieldset.querySelector('.personal-repeat-days');
      const weekend = toggle.closest('form').querySelector('.personal-weekend-option');
      const update = () => {
        days.hidden = !toggle.checked;
        days.querySelectorAll('input').forEach(input => {input.disabled = !toggle.checked;});
        if (weekend) weekend.hidden = toggle.checked;
      };
      toggle.addEventListener('change', update); update();
    });
  }
  document.addEventListener('DOMContentLoaded', init);
  document.addEventListener('personal:navigated', init);
document.addEventListener('personal:event-dialog-rendered', init);
})();
