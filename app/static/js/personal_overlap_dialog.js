(() => {
  const bound = new WeakSet();
  function init() {
    document.querySelectorAll('[data-overlap-dialog]').forEach(dialog => {
      if (bound.has(dialog)) return;
      bound.add(dialog);
      dialog.querySelector('[data-overlap-close]').addEventListener('click', () => dialog.close());
      dialog.querySelector('[data-overlap-confirm]').addEventListener('click', () => {
        dialog.close();
        dialog.closest('.personal-form-page')?.querySelector('.personal-event-form')?.requestSubmit();
      });
      dialog.showModal();
    });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
  document.addEventListener('personal:navigated', init);
  document.addEventListener('personal:event-dialog-rendered', init);
})();
