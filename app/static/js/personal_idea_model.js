/* Project pending commands over confirmed ideas; replay acknowledgements never erase later edits. */
(() => {
  function rebase(state) {
    const items = new Map((state.confirmed_ideas || []).map(item => [item.id, structuredClone(item)]));
    for (const op of state.outbox || []) {
      if (op.state === 'failed') continue;
      const command = op.command, old = items.get(command.idea_id);
      if (command.action === 'abandon') {
        if (old) items.set(old.id, {...old, status: 'abandoned', updated_at: op.created_at});
      } else if (command.action === 'create' || old) {
        items.set(command.idea_id, {...old, id: command.idea_id, title: command.title,
          note: command.note, reference_links: command.reference_links, status: 'pending',
          created_at: old?.created_at || op.created_at, updated_at: op.created_at});
      }
    }
    state.ideas = [...items.values()].sort((a,b) => a.created_at.localeCompare(b.created_at) || a.id.localeCompare(b.id));
    return state;
  }
  function ack(state, op, reply) {
    if (!state.outbox.some(item => item.id === op.id)) return state;
    state.confirmed_ideas = state.confirmed_ideas.filter(item => item.id !== reply.idea.id).concat(reply.idea);
    state.outbox = state.outbox.filter(item => item.id !== op.id);
    return rebase(state);
  }
  function fail(state, op, message, status) {
    let found = false;
    for (const item of state.outbox) {
      if (item.id === op.id) found = true;
      if (found && item.command.idea_id === op.command.idea_id)
        Object.assign(item, {state: 'failed', error: message, status});
    }
    return rebase(state);
  }
  const api = {rebase, ack, fail};
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else window.awesomeInterviewIdeaModel = api;
})();
