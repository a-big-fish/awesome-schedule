(() => {
  const dialog = document.querySelector('[data-overlap-dialog]');
  if (!dialog) return;
  dialog.querySelector('[data-overlap-close]').addEventListener('click', () => dialog.close());
  dialog.querySelector('[data-overlap-confirm]').addEventListener('click', () => {
    dialog.close();
    document.querySelector('.personal-event-form')?.requestSubmit();
  });
  dialog.showModal();
})();
