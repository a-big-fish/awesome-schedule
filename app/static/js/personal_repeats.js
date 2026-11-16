(() => {
  function init() {
    document.querySelectorAll('[data-repeat-enabled]').forEach(toggle => {
      // A cached shell serialises data-* markers from the live DOM but never its listeners,
      // so remember initialisation on the node itself.
      if (toggle.__repeatBound) return;
      toggle.__repeatBound = true;
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
