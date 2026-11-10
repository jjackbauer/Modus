'use strict';

// Tholdem spectator viewer. Same-origin only: every request goes to a relative or root-relative
// path on the loopback host that served this page. No external asset or network call is made.
// Artifact content (ids, prompts, reasons, ledger fields) is always written with textContent.

const EMPTY_STATES = {
  live: 'No live match yet — pick a seed and press Start',
  replay: 'Select a recorded session to replay',
  strategies: 'No strategies recorded yet — run the training loop',
  ledger: 'No promotions yet',
};

const SUITS = {
  c: { name: 'clubs', glyph: '♣', color: 'black' },
  d: { name: 'diamonds', glyph: '♦', color: 'red' },
  h: { name: 'hearts', glyph: '♥', color: 'red' },
  s: { name: 'spades', glyph: '♠', color: 'black' },
};

const RANK_LABELS = {
  A: 'A', K: 'K', Q: 'Q', J: 'J', T: '10',
  9: '9', 8: '8', 7: '7', 6: '6', 5: '5', 4: '4', 3: '3', 2: '2',
};

const RANK_NAMES = {
  A: 'Ace', K: 'King', Q: 'Queen', J: 'Jack', T: 'Ten',
  9: 'Nine', 8: 'Eight', 7: 'Seven', 6: 'Six', 5: 'Five', 4: 'Four', 3: 'Three', 2: 'Two',
};

const ACTION_CLASSES = {
  Fold: 'action--fold',
  Check: 'action--check',
  Call: 'action--call',
  Bet: 'action--bet',
  Raise: 'action--raise',
  AllIn: 'action--allin',
};

const STREET_LABELS = {
  0: 'Preflop', 1: 'Flop', 2: 'Turn', 3: 'River', 4: 'Showdown', 5: 'Complete',
  preflop: 'Preflop', flop: 'Flop', turn: 'Turn', river: 'River',
  showdown: 'Showdown', complete: 'Complete',
};

const LEDGER_COLUMNS = [
  'Iteration', 'Champion', 'Challenger', 'Promoted', 'Reason',
  'Win rate', 'bb/100', 'LCB Δ', 'Samples',
];

const VIEW_KEYS = { 1: 'live', 2: 'replay', 3: 'strategies', 4: 'ledger', 5: 'training' };

// Mirrors Wip.Tholdem.Web.SessionSeedPolicy.MinimumSeed. Seeds are 64-bit, so the form and the
// request keep them as decimal strings: Number would round (and can overflow) the top bits.
const MINIMUM_SEED = 1099511627776n;

// The training job is polled independently of the live match feed so a running job never blocks
// the live table, replay or the artifact tabs.
const TRAINING_POLL_MS = 250;
const TRAINING_POLL_FAILURE_LIMIT = 12;
const UNSIGNED_64_MAX = 18446744073709551615n;

function randomSeedText() {
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const words = new Uint32Array(2);
    crypto.getRandomValues(words);
    let seed = (BigInt(words[0]) << 32n) | BigInt(words[1]);
    if (seed < MINIMUM_SEED) {
      seed += MINIMUM_SEED;
    }
    return seed.toString();
  }
  return MINIMUM_SEED.toString();
}

const state = {
  view: 'live',
  liveId: null,
  liveInfo: null,
  liveTimer: null,
  liveSnapshot: null,
  liveStatus: null,
  pollSpeed: 300,
  sessions: [],
  selectedSession: null,
  replayHand: null,
  replayHandNumber: 1,
  strategies: [],
  selectedStrategyId: null,
  championId: null,
  renderedActionMatch: null,
  trainingJobId: null,
  trainingTimer: null,
  trainingSnapshot: null,
  trainingEntriesKey: null,
  trainingPromotionDone: false,
  trainingCancelRequested: false,
  trainingPollFailures: 0,
  rsiJobId: null,
  rsiTimer: null,
  rsiSnapshot: null,
  rsiCancelRequested: false,
  rsiPollFailures: 0,
  rsiPromotionDone: false,
};

function byId(id) {
  return document.getElementById(id);
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) {
    node.className = className;
  }
  if (text !== undefined && text !== null) {
    node.textContent = String(text);
  }
  return node;
}

function clear(node) {
  if (node) {
    node.textContent = '';
  }
}

// Live regions only announce real changes; rewriting identical text would be noisy.
function setText(node, text) {
  if (!node) {
    return;
  }
  const value = text === undefined || text === null ? '' : String(text);
  if (node.textContent !== value) {
    node.textContent = value;
  }
}

function capitalize(text) {
  if (!text || typeof text !== 'string') {
    return '';
  }
  return text.charAt(0).toUpperCase() + text.slice(1);
}

function formatNumber(value, digits) {
  return typeof value === 'number' && isFinite(value) ? value.toFixed(digits) : '—';
}

function formatPercent(value) {
  return typeof value === 'number' && isFinite(value) ? (value * 100).toFixed(1) + '%' : '—';
}

// --- CSS-drawn playing cards ---------------------------------------------------------------

function createEmptyCard(label) {
  const card = el('span', 'card card--empty');
  card.setAttribute('aria-label', label || 'Empty card slot');
  return card;
}

function createCardBack() {
  const card = el('span', 'card card--back');
  card.setAttribute('aria-label', 'Face-down card');
  return card;
}

function createCard(code) {
  if (typeof code !== 'string' || code.length !== 2) {
    return createEmptyCard('Empty card slot');
  }
  const rank = code.charAt(0).toUpperCase();
  const suit = SUITS[code.charAt(1).toLowerCase()];
  const rankLabel = RANK_LABELS[rank];
  const rankName = RANK_NAMES[rank];
  if (!suit || !rankLabel || !rankName) {
    return createEmptyCard('Empty card slot');
  }
  const card = el('span', 'card card--' + suit.name + ' card--' + suit.color);
  card.dataset.card = code;
  card.setAttribute('aria-label', rankName + ' of ' + suit.name);
  card.appendChild(el('span', 'card__rank', rankLabel));
  card.appendChild(el('span', 'card__suit', suit.glyph));
  return card;
}

// Card containers only rebuild when their contents actually change; this keeps the board live
// region quiet and avoids re-announcing unchanged cards on every poll.
function renderBoard(container, cards) {
  const list = cards || [];
  const key = list.join(',');
  if (container.dataset.cards === key && container.childElementCount === 5) {
    return;
  }
  container.dataset.cards = key;
  clear(container);
  for (let index = 0; index < 5; index += 1) {
    const code = list.length > index ? list[index] : null;
    container.appendChild(code ? createCard(code) : createEmptyCard('Empty board slot'));
  }
}

function renderHoleCards(container, cards) {
  const list = cards || [];
  const key = list.join(',');
  if (container.dataset.cards === key && container.childElementCount === 2) {
    return;
  }
  container.dataset.cards = key;
  clear(container);
  for (let index = 0; index < 2; index += 1) {
    const code = list.length > index ? list[index] : null;
    container.appendChild(code ? createCard(code) : createCardBack());
  }
}

// Deliberate idle state: no cards at all, not two implied face-down hands.
function clearCardSlot(container) {
  if (container.dataset.cards === '__none' && container.childElementCount === 0) {
    return;
  }
  container.dataset.cards = '__none';
  clear(container);
}

// --- Table rendering (shared by the live feed and the recorded replay) ----------------------

function streetLabel(street) {
  if (typeof street === 'number') {
    return STREET_LABELS[street] || 'Street';
  }
  if (typeof street === 'string') {
    return STREET_LABELS[street.toLowerCase()] || capitalize(street);
  }
  return 'Street';
}

function actionClass(kind) {
  return ACTION_CLASSES[kind] || 'action--other';
}

function createHandSeparator(handNumber) {
  const row = el('li', 'action-hand');
  row.dataset.hand = String(handNumber);
  row.appendChild(el('span', 'action-hand-label', 'Hand ' + handNumber));
  return row;
}

function createActionRow(action) {
  const street = streetLabel(action.street);
  const kind = typeof action.kind === 'string' ? action.kind : '—';
  const row = el('li', 'action-row ' + actionClass(kind));
  if (action.handNumber !== undefined && action.handNumber !== null) {
    row.dataset.hand = String(action.handNumber);
  }
  row.dataset.street = street;
  row.dataset.seat = String(action.seat);
  row.dataset.kind = kind;
  row.dataset.amount = String(action.amount);
  row.dataset.potAfter = String(action.potAfter);
  row.appendChild(el('span', 'chip chip--' + street.toLowerCase(), street));
  row.appendChild(el('span', 'action-seat', 'Seat ' + action.seat));
  row.appendChild(el('span', 'action-kind', kind));
  row.appendChild(el('span', 'action-amount', action.amount > 0 ? String(action.amount) : '—'));
  row.appendChild(el('span', 'action-pot', 'pot ' + action.potAfter));
  return row;
}

// The retained spectator tail spans hands, so a "Hand N" separator opens each hand and every row
// carries data-hand. Without it a row cannot be attributed to the hand named in the title.
function appendActionRows(container, actions, startIndex, lastHand) {
  let hand = lastHand;
  const rows = actions || [];
  for (let index = startIndex || 0; index < rows.length; index += 1) {
    const action = rows[index];
    const actionHand = Number(action.handNumber);
    if (Number.isFinite(actionHand) && actionHand > 0 && actionHand !== hand) {
      container.appendChild(createHandSeparator(actionHand));
      hand = actionHand;
    }
    container.appendChild(createActionRow(action));
  }
  return hand;
}

function createSettlementRow(settlement) {
  const winners = settlement.winners || [];
  const seatText = winners.length === 0 ? 'No winner' : 'Seat ' + winners.join(', Seat ');
  const row = el('li', 'action-row action--settle');
  row.dataset.settlement = 'true';
  row.dataset.potAwarded = String(settlement.potAwarded);
  row.appendChild(el('span', 'chip chip--settle', 'Result'));
  row.appendChild(el('span', 'action-seat', 'Settlement'));
  row.appendChild(el('span', 'action-kind', seatText + ' awarded'));
  row.appendChild(el('span', 'action-amount', String(settlement.potAwarded)));
  row.appendChild(el('span', 'action-pot', 'pot awarded ' + settlement.potAwarded));
  return row;
}

function isScrolledToBottom(container) {
  return container.scrollHeight - container.scrollTop - container.clientHeight <= 8;
}

function scrollActionLog(container) {
  container.scrollTop = container.scrollHeight;
}

function resetActionLog(container) {
  clear(container);
  container.dataset.count = '0';
  container.dataset.lastHand = '0';
}

function renderActions(container, actions, options) {
  const settings = options || {};
  const atBottom = isScrolledToBottom(container);
  const empty = byId('action-empty');
  resetActionLog(container);
  const list = actions || [];
  if (list.length === 0) {
    empty.hidden = false;
    return;
  }
  empty.hidden = true;
  const lastHand = appendActionRows(container, list, 0, 0);
  container.dataset.count = String(list.length);
  container.dataset.lastHand = String(lastHand);
  // Follow only when the reader was already at the bottom; never snap while they read older rows.
  if (settings.follow !== false && atBottom) {
    scrollActionLog(container);
  }
}

function appendLiveActions(container, actions) {
  const list = actions || [];
  const rendered = Number(container.dataset.count || '0');
  const atBottom = isScrolledToBottom(container);
  if (list.length < rendered) {
    // Retention truncates the tail or the view was rebuilt: replace the whole list.
    renderActions(container, list, { follow: atBottom });
    return;
  }
  if (list.length === rendered) {
    return;
  }
  byId('action-empty').hidden = true;
  const lastHand = appendActionRows(container, list, rendered, Number(container.dataset.lastHand || '0'));
  container.dataset.count = String(list.length);
  container.dataset.lastHand = String(lastHand);
  if (atBottom) {
    scrollActionLog(container);
  }
}

// Live only: append the new tail so a role="log" region announces additions rather than replaying
// the whole retained log on every poll.
function syncLiveActions(container, actions, matchId) {
  if (state.renderedActionMatch !== matchId) {
    renderActions(container, actions, { follow: true });
    state.renderedActionMatch = matchId;
    return;
  }
  appendLiveActions(container, actions);
}

// One panel per seat. The markup comes from the inert #seat-template; every snapshot-provided
// value (seat index, agent, stack, cards, hand name, badges) is written with textContent.
function ensureSeatPanels(count) {
  const grid = byId('seat-grid');
  const template = byId('seat-template');
  if (!grid || !template) {
    return;
  }
  if (grid.dataset.count === String(count) && grid.querySelectorAll('.seat').length === count) {
    return;
  }
  clear(grid);
  for (let index = 0; index < count; index += 1) {
    const fragment = document.importNode(template.content, true);
    const panel = fragment.querySelector('.seat');
    panel.dataset.seat = String(index);
    setText(panel.querySelector('.seat-name'), 'Seat ' + index);
    grid.appendChild(fragment);
  }
  grid.dataset.count = String(count);
  byId('live-table').dataset.seats = String(count);
}

function renderSeat(seat, dealerSeat) {
  const element = document.querySelector('.seat[data-seat="' + seat.index + '"]');
  if (!element) {
    return;
  }
  const acting = Boolean(seat.isActive);
  element.classList.toggle('is-active', acting);
  element.classList.toggle('is-last-action', Boolean(seat.isLastAction));
  element.classList.toggle('is-winner', Boolean(seat.isWinner));
  element.classList.toggle('is-folded', Boolean(seat.isFolded));
  element.classList.toggle('is-allin', Boolean(seat.isAllIn));
  if (acting) {
    element.setAttribute('aria-current', 'true');
  } else {
    element.removeAttribute('aria-current');
  }
  const turn = element.querySelector('[data-turn]');
  if (turn) {
    turn.hidden = !acting;
    setText(turn, acting ? 'To act' : '');
  }
  const last = element.querySelector('[data-last-action]');
  if (last) {
    last.hidden = !seat.isLastAction;
    setText(last, seat.isLastAction ? 'Last action' : '');
  }
  const folded = element.querySelector('[data-folded]');
  if (folded) {
    folded.hidden = !seat.isFolded;
    setText(folded, seat.isFolded ? 'Folded' : '');
  }
  const allIn = element.querySelector('[data-allin]');
  if (allIn) {
    allIn.hidden = !seat.isAllIn;
    setText(allIn, seat.isAllIn ? 'All-in' : '');
  }
  setText(element.querySelector('[data-seat-agent]'), seat.agent || '—');
  setText(element.querySelector('[data-seat-stack]'), String(seat.stack));
  setText(element.querySelector('[data-seat-hand-name]'), seat.handName || 'Preflop');
  const isDealer = typeof seat.isDealer === 'boolean' ? seat.isDealer : seat.index === dealerSeat;
  element.querySelector('[data-dealer]').hidden = !isDealer;
  element.querySelector('[data-winner]').hidden = !seat.isWinner;
  renderHoleCards(element.querySelector('[data-seat-cards]'), seat.holeCards);
}

function renderTable(model) {
  byId('table-empty').hidden = true;
  byId('live-table').classList.remove('is-empty');
  setText(byId('table-title'), model.title);
  renderBoard(byId('board'), model.board || []);
  setText(byId('pot'), model.potText);
  setText(byId('street'), model.street);
  const toCall = byId('to-call');
  if (model.toCall && model.toCall.amount > 0) {
    toCall.hidden = false;
    setText(toCall, 'To call ' + model.toCall.amount
      + ' · pot odds ' + formatPercent(model.toCall.odds));
  } else {
    toCall.hidden = true;
    setText(toCall, '');
  }
  const seats = model.seats || [];
  ensureSeatPanels(seats.length);
  for (const seat of seats) {
    renderSeat(seat, model.dealerSeat);
  }
  const log = byId('action-log');
  if (model.liveMatchId) {
    syncLiveActions(log, model.actions || [], model.liveMatchId);
  } else {
    state.renderedActionMatch = null;
    renderActions(log, model.actions || [], { follow: true });
  }
  if (model.settlement && model.settlement.potAwarded > 0) {
    const settledAtBottom = isScrolledToBottom(log);
    log.appendChild(createSettlementRow(model.settlement));
    if (settledAtBottom) {
      scrollActionLog(log);
    }
  }
}

function showTableEmpty() {
  const empty = byId('table-empty');
  setText(empty, state.view === 'replay' ? EMPTY_STATES.replay : EMPTY_STATES.live);
  empty.hidden = false;
  setText(byId('table-title'), state.view === 'replay' ? 'Replay' : 'Live table');
  byId('live-table').classList.add('is-empty');
  setText(byId('pot'), '—');
  setText(byId('street'), '—');
  const toCall = byId('to-call');
  toCall.hidden = true;
  setText(toCall, '');
  byId('action-empty').hidden = false;
  resetActionLog(byId('action-log'));
  state.renderedActionMatch = null;
  renderBoard(byId('board'), []);
  for (const seat of document.querySelectorAll('.seat')) {
    seat.classList.remove('is-winner');
    seat.classList.remove('is-active');
    seat.classList.remove('is-last-action');
    seat.classList.remove('is-folded');
    seat.classList.remove('is-allin');
    seat.removeAttribute('aria-current');
    setText(seat.querySelector('[data-seat-agent]'), '—');
    setText(seat.querySelector('[data-seat-stack]'), '—');
    setText(seat.querySelector('[data-seat-hand-name]'), '—');
    seat.querySelector('[data-dealer]').hidden = true;
    seat.querySelector('[data-winner]').hidden = true;
    const turn = seat.querySelector('[data-turn]');
    if (turn) {
      turn.hidden = true;
      setText(turn, '');
    }
    const last = seat.querySelector('[data-last-action]');
    if (last) {
      last.hidden = true;
      setText(last, '');
    }
    const folded = seat.querySelector('[data-folded]');
    if (folded) {
      folded.hidden = true;
      setText(folded, '');
    }
    const allIn = seat.querySelector('[data-allin]');
    if (allIn) {
      allIn.hidden = true;
      setText(allIn, '');
    }
    clearCardSlot(seat.querySelector('[data-seat-cards]'));
    seat.remove();
  }
  const grid = byId('seat-grid');
  if (grid) {
    grid.dataset.count = '0';
  }
  byId('live-table').removeAttribute('data-seats');
}

// The ring marks the seat to act, which is a snapshot field — never the last action's seat.
// Completed hands expose actingSeat -1, so no seat is ringed; the last actor keeps a labelled cue.
function liveSeatState(snapshot) {
  const live = snapshot.status === 'running' || snapshot.status === 'paused';
  const acting = live && snapshot.isComplete !== true
    && typeof snapshot.actingSeat === 'number' && snapshot.actingSeat >= 0
    ? snapshot.actingSeat
    : -1;
  const lastAction = typeof snapshot.lastActionSeat === 'number' && snapshot.lastActionSeat >= 0
    ? snapshot.lastActionSeat
    : -1;
  return { acting: acting, lastAction: lastAction };
}

// One panel per snapshot seat. The server sends `seats` for every configured table size (2–6);
// a pre-multiway server without the member is rendered from the legacy two-seat fields.
function liveSeats(snapshot) {
  const complete = snapshot.isComplete === true;
  const winners = snapshot.winners || [];
  const seatState = liveSeatState(snapshot);
  if (Array.isArray(snapshot.seats) && snapshot.seats.length > 0) {
    return snapshot.seats.map(function (seat) {
      return {
        index: seat.seat,
        agent: seat.agent || '—',
        stack: typeof seat.stack === 'number' ? seat.stack : 0,
        holeCards: seat.holeCards || [],
        handName: seat.handName || 'Preflop',
        isWinner: complete && (seat.winner === true || winners.indexOf(seat.seat) >= 0),
        isActive: seatState.acting === seat.seat,
        isLastAction: seatState.lastAction === seat.seat,
        isFolded: seat.folded === true,
        isAllIn: seat.allIn === true,
        isDealer: seat.dealer === true,
      };
    });
  }

  const names = snapshot.handNames && snapshot.handNames.length === 2
    ? snapshot.handNames
    : ['Preflop', 'Preflop'];
  const stacks = snapshot.stacks || [];
  const agents = state.liveInfo
    ? [state.liveInfo.seat0Agent, state.liveInfo.seat1Agent]
    : ['—', '—'];
  return [
    {
      index: 0,
      agent: agents[0],
      stack: stacks.length > 0 ? stacks[0] : 0,
      holeCards: snapshot.holeCardsSeat0 || [],
      handName: names[0],
      isWinner: complete && winners.indexOf(0) >= 0,
      isActive: seatState.acting === 0,
      isLastAction: seatState.lastAction === 0,
      isFolded: false,
      isAllIn: false,
      isDealer: snapshot.buttonSeat === 0,
    },
    {
      index: 1,
      agent: agents[1],
      stack: stacks.length > 1 ? stacks[1] : 0,
      holeCards: snapshot.holeCardsSeat1 || [],
      handName: names[1],
      isWinner: complete && winners.indexOf(1) >= 0,
      isActive: seatState.acting === 1,
      isLastAction: seatState.lastAction === 1,
      isFolded: false,
      isAllIn: false,
      isDealer: snapshot.buttonSeat === 1,
    },
  ];
}

function renderLive(snapshot) {
  const complete = snapshot.isComplete === true;
  const potAwarded = typeof snapshot.potAwarded === 'number' ? snapshot.potAwarded : 0;
  const dealerSeat = typeof snapshot.buttonSeat === 'number' ? snapshot.buttonSeat : -1;
  const toCall = typeof snapshot.amountToCall === 'number' && snapshot.amountToCall > 0
    ? { amount: snapshot.amountToCall, odds: snapshot.potOdds }
    : null;
  const potText = complete && potAwarded > 0
    ? 'Pot awarded ' + potAwarded
    : 'Pot ' + snapshot.pot;

  renderTable({
    title: snapshot.handNumber > 0 ? 'Live — hand ' + snapshot.handNumber : 'Live table',
    board: snapshot.board || [],
    potText: potText,
    street: complete ? 'Complete' : capitalize(snapshot.street),
    toCall: toCall,
    dealerSeat: dealerSeat,
    seats: liveSeats(snapshot),
    actions: snapshot.actions || [],
    liveMatchId: snapshot.id,
  });
}

function replayAgents(hand, seatCount) {
  const expected = typeof seatCount === 'number' && seatCount > 0 ? seatCount : 2;
  if (hand && Array.isArray(hand.agents) && hand.agents.length >= expected) {
    return hand.agents;
  }
  const agents = state.selectedSession && Array.isArray(state.selectedSession.agents)
    ? state.selectedSession.agents
    : null;
  if (agents && agents.length >= expected) {
    return agents;
  }
  const fallback = [];
  for (let index = 0; index < expected; index += 1) {
    fallback.push('—');
  }
  return fallback;
}

// One replay panel per recorded seat: `holeCards` is the v2 per-seat list, with the legacy
// two-seat fields as the fallback for a pre-multiway hand record.
function replaySeats(hand) {
  const cards = Array.isArray(hand.holeCards) && hand.holeCards.length > 0
    ? hand.holeCards
    : [hand.holeCardsSeat0 || [], hand.holeCardsSeat1 || []];
  const names = hand.handNames && hand.handNames.length === cards.length ? hand.handNames : null;
  const winners = hand.winners || [];
  const stacks = hand.finalStacks || [];
  const agents = replayAgents(hand, cards.length);
  return cards.map(function (holeCards, index) {
    return {
      index: index,
      agent: agents[index] || '—',
      stack: stacks.length > index ? stacks[index] : 0,
      holeCards: holeCards || [],
      handName: names ? names[index] : 'Preflop',
      isWinner: winners.indexOf(index) >= 0,
      isActive: false,
      isLastAction: false,
      isFolded: false,
      isAllIn: false,
      isDealer: hand.buttonSeat === index,
    };
  });
}

function renderReplay(hand) {
  const winners = hand.winners || [];
  renderTable({
    title: 'Replay — hand ' + hand.handNumber,
    board: hand.board || [],
    potText: 'Pot awarded ' + hand.potAwarded,
    street: hand.showdown ? 'Showdown' : 'Complete',
    toCall: null,
    dealerSeat: typeof hand.buttonSeat === 'number' ? hand.buttonSeat : -1,
    seats: replaySeats(hand),
    actions: hand.actions || [],
    settlement: hand.potAwarded > 0
      ? { winners: winners, potAwarded: hand.potAwarded }
      : null,
  });
}

// --- Live match feed ------------------------------------------------------------------------

function setLiveMessage(text) {
  setText(byId('live-meta'), text);
}

function liveMessage(snapshot) {
  const hashes = snapshot.handHashes ? snapshot.handHashes.length : 0;
  return 'Match ' + snapshot.id
    + ' · ' + snapshot.status
    + ' · hand ' + snapshot.handNumber
    + ' · ' + hashes + ' hashes';
}

function schedulePoll() {
  if (state.liveTimer) {
    clearTimeout(state.liveTimer);
  }
  state.liveTimer = setTimeout(pollLive, state.pollSpeed);
}

async function pollLive() {
  state.liveTimer = null;
  if (!state.liveId) {
    return;
  }
  try {
    const response = await fetch('/api/live/' + encodeURIComponent(state.liveId));
    if (response.status === 404) {
      setLiveMessage('Live match not found.');
      state.liveId = null;
      return;
    }
    if (!response.ok) {
      schedulePoll();
      return;
    }
    const snapshot = await response.json();
    state.liveSnapshot = snapshot;
    state.liveStatus = snapshot.status;
    if (state.view === 'live') {
      renderLive(snapshot);
    }
    setLiveMessage(liveMessage(snapshot));
    if (snapshot.status === 'running' || snapshot.status === 'paused') {
      schedulePoll();
    } else {
      refreshStatus();
    }
  } catch (error) {
    schedulePoll();
  }
}

async function startLiveMatch(event) {
  event.preventDefault();
  const seedText = byId('seed').value.trim();
  if (!/^[0-9]+$/.test(seedText)) {
    setLiveMessage('Seed must be a non-negative integer.');
    return;
  }
  const seats = Number(byId('seats').value) || 2;
  const seat0Agent = byId('seat0-agent').value;
  const seat1Agent = byId('seat1-agent').value;
  const agents = [];
  for (let index = 0; index < seats; index += 1) {
    agents.push(index % 2 === 0 ? seat0Agent : seat1Agent);
  }
  const body = {
    seed: seedText,
    hands: Number(byId('hands').value),
    delayMs: Number(byId('delay').value),
    seats: seats,
    agents: agents,
    seat0Agent: seat0Agent,
    seat1Agent: seat1Agent,
    allowWeakSeed: byId('allow-weak-seed').checked,
  };
  try {
    const response = await fetch('/api/live', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      let detail = null;
      try {
        detail = await response.json();
      } catch (error) {
        detail = null;
      }
      if (detail && detail.error === 'weak-seed') {
        setLiveMessage('Seed ' + seedText + ' is below the minimum ' + String(MINIMUM_SEED)
          + ' — tick "Allow weak seed" for a demo or test.');
      } else {
        setLiveMessage('Start failed with status ' + response.status + '.');
      }
      return;
    }
    const started = await response.json();
    state.liveInfo = started;
    state.liveId = started.id;
    state.liveSnapshot = null;
    state.liveStatus = 'running';
    state.renderedActionMatch = null;
    resetActionLog(byId('action-log'));
    byId('action-empty').hidden = true;
    setLiveMessage('Match ' + started.id + ' · running');
    selectView('live');
    await refreshStatus();
    schedulePoll();
  } catch (error) {
    setLiveMessage('Start request failed.');
  }
}

async function sendLiveControl(path, method) {
  if (!state.liveId) {
    return;
  }
  try {
    const response = await fetch(
      '/api/live/' + encodeURIComponent(state.liveId) + path,
      { method: method },
    );
    if (response.ok || response.status === 204) {
      schedulePoll();
    } else {
      setLiveMessage('Control request failed with status ' + response.status + '.');
    }
  } catch (error) {
    setLiveMessage('Control request failed.');
  }
}

function togglePause() {
  if (!state.liveId) {
    return;
  }
  if (state.liveStatus === 'paused') {
    sendLiveControl('/resume', 'POST');
  } else if (state.liveStatus === 'running') {
    sendLiveControl('/pause', 'POST');
  }
}

async function refreshStatus() {
  try {
    const response = await fetch('/api/status');
    if (!response.ok) {
      return;
    }
    const status = await response.json();
    const line = byId('status');
    setText(line, 'Spectator · ' + status.dataDirectory
      + ' · sessions ' + status.sessions
      + ' · strategies ' + status.strategies
      + ' · active matches ' + status.liveMatches);
    line.dataset.offline = String(status.offline);
  } catch (error) {
    setLiveMessage('Status request failed.');
  }
}

// --- Recorded sessions and replay -----------------------------------------------------------

async function loadSessions() {
  try {
    const response = await fetch('/api/sessions');
    if (!response.ok) {
      return;
    }
    const payload = await response.json();
    state.sessions = payload.sessions || [];
    const list = byId('sessions');
    clear(list);
    if (state.sessions.length === 0) {
      list.appendChild(el('li', 'empty', 'No recorded sessions.'));
      return;
    }
    for (const session of state.sessions) {
      const item = el('li', 'session-item');
      const button = el('button', 'session-button');
      button.type = 'button';
      button.setAttribute('aria-pressed', 'false');
      button.appendChild(el('span', 'session-id', session.sessionId));
      button.appendChild(el('span', 'session-meta', session.handCount + ' hands · seed ' + session.seed));
      button.addEventListener('click', function () {
        selectSession(session, button);
      });
      item.appendChild(button);
      list.appendChild(item);
    }
  } catch (error) {
    setLiveMessage('Session list request failed.');
  }
}

function selectSession(session, button) {
  state.selectedSession = session;
  state.replayHand = null;
  for (const candidate of byId('sessions').querySelectorAll('button')) {
    const active = candidate === button;
    candidate.classList.toggle('is-active', active);
    candidate.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
  const handCount = session.handCount > 0 ? session.handCount : session.hands;
  const total = handCount > 0 ? handCount : 1;
  const scrubber = byId('replay-scrubber');
  scrubber.min = '1';
  scrubber.max = String(total);
  scrubber.value = '1';
  byId('replay-hand').textContent = 'Hand 1 of ' + total;
  selectView('replay');
  loadReplayHand(1);
}

async function loadReplayHand(handNumber) {
  if (!state.selectedSession) {
    showTableEmpty();
    return;
  }
  const url = '/api/sessions/' + encodeURIComponent(state.selectedSession.sessionId)
    + '/hands/' + encodeURIComponent(String(handNumber));
  try {
    const response = await fetch(url);
    if (!response.ok) {
      byId('replay-hand').textContent = 'Hand ' + handNumber + ' is not available.';
      showTableEmpty();
      return;
    }
    const hand = await response.json();
    state.replayHand = hand;
    state.replayHandNumber = hand.handNumber;
    const total = state.selectedSession.handCount || state.selectedSession.hands;
    byId('replay-hand').textContent = 'Hand ' + hand.handNumber + ' of ' + total;
    if (state.view === 'replay') {
      renderReplay(hand);
    }
  } catch (error) {
    byId('replay-hand').textContent = 'Replay request failed.';
  }
}

function stepReplayHand(delta) {
  if (!state.selectedSession) {
    return;
  }
  const scrubber = byId('replay-scrubber');
  const max = Number(scrubber.max) || 1;
  const current = state.replayHandNumber || Number(scrubber.value) || 1;
  const next = Math.min(max, Math.max(1, current + delta));
  if (next === current) {
    return;
  }
  scrubber.value = String(next);
  selectView('replay');
  loadReplayHand(next);
}

// --- Strategies -----------------------------------------------------------------------------

async function loadChampion() {
  try {
    const response = await fetch('/api/champion');
    if (response.status === 404) {
      state.championId = null;
      renderTrainingChampion(null);
      return;
    }
    if (!response.ok) {
      renderTrainingChampionEmpty('Champion request failed.');
      return;
    }
    const champion = await response.json();
    state.championId = typeof champion.strategyId === 'string' ? champion.strategyId : null;
    renderTrainingChampion(champion);
  } catch (error) {
    state.championId = null;
    renderTrainingChampionEmpty('Champion request failed.');
  }
}

async function loadStrategies() {
  try {
    const response = await fetch('/api/strategies');
    if (!response.ok) {
      state.strategies = [];
      renderStrategyList();
      loadChampion();
      return;
    }
    const payload = await response.json();
    state.strategies = payload.strategies || [];
    await loadChampion();
    renderStrategyList();
  } catch (error) {
    setLiveMessage('Strategy list request failed.');
    loadChampion();
  }
}

function strategyExists(id) {
  return state.strategies.some(function (entry) {
    return entry.id === id;
  });
}

// The list and the detail card are built from created elements; artifact text is never markup.
function renderStrategyList() {
  const list = byId('strategies');
  clear(list);
  byId('strategies-empty').hidden = state.strategies.length > 0;
  if (state.strategies.length === 0) {
    state.selectedStrategyId = null;
    renderStrategyDetail(null, null);
    return;
  }

  let selectedId = state.selectedStrategyId;
  if (!selectedId || !strategyExists(selectedId)) {
    selectedId = state.championId && strategyExists(state.championId)
      ? state.championId
      : state.strategies[0].id;
  }

  for (const entry of state.strategies) {
    const item = el('li', 'strategy-item');
    const button = el('button', 'strategy-button');
    button.type = 'button';
    button.dataset.strategyId = entry.id;
    button.setAttribute('aria-pressed', entry.id === selectedId ? 'true' : 'false');
    button.classList.toggle('is-active', entry.id === selectedId);
    const payload = entry.payload || {};
    const version = typeof payload.version === 'number' ? 'version ' + payload.version : 'version ?';
    const parent = payload.parentId ? payload.parentId : 'none';
    button.appendChild(el('span', 'strategy-id', entry.id));
    button.appendChild(el('span', 'strategy-meta', version + ' · parent ' + parent));
    if (entry.id === state.championId) {
      button.appendChild(el('span', 'pill pill--champion', 'Champion'));
    }
    button.addEventListener('click', function () {
      selectStrategy(entry.id);
    });
    item.appendChild(button);
    list.appendChild(item);
  }

  selectStrategy(selectedId);
}

function selectStrategy(id) {
  state.selectedStrategyId = id;
  let entry = null;
  for (const candidate of state.strategies) {
    if (candidate.id === id) {
      entry = candidate;
      break;
    }
  }
  for (const button of byId('strategies').querySelectorAll('button')) {
    const active = button.dataset.strategyId === id;
    button.classList.toggle('is-active', active);
    button.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
  renderStrategyDetail(entry ? entry.id : null, entry ? entry.payload : null);
}

function formatParameter(value) {
  return typeof value === 'number' && isFinite(value) ? value.toFixed(2) : '—';
}

function renderStrategyDetail(id, payload) {
  const detail = byId('strategy-detail');
  detail.textContent = '';
  if (!id || !payload) {
    detail.appendChild(el('p', 'empty-state', 'Select a strategy to inspect it.'));
    return;
  }

  const card = el('article', 'strategy-card');
  const head = el('header', 'strategy-card-head');
  head.appendChild(el('h3', 'strategy-card-id', id));
  if (id === state.championId) {
    head.appendChild(el('span', 'pill pill--champion', 'Champion'));
  }
  card.appendChild(head);

  const version = typeof payload.version === 'number' ? String(payload.version) : '—';
  const parent = payload.parentId ? payload.parentId : 'none (root)';
  card.appendChild(el('p', 'strategy-card-meta', 'Version ' + version + ' · Parent ' + parent));

  const parameters = payload.parameters || {};
  const rows = [
    ['Raise threshold', parameters.raiseThreshold],
    ['Call threshold', parameters.callThreshold],
    ['Bluff frequency', parameters.bluffFrequency],
    ['Sizing multiplier', parameters.sizingMultiplier],
  ];
  const list = el('dl', 'param-list');
  for (const row of rows) {
    const line = el('div', 'param-row');
    line.appendChild(el('dt', null, row[0]));
    line.appendChild(el('dd', null, formatParameter(row[1])));
    list.appendChild(line);
  }
  card.appendChild(list);

  card.appendChild(el('p', 'strategy-prompt-label', 'Prompt'));
  const prompt = typeof payload.promptText === 'string' ? payload.promptText : '—';
  card.appendChild(el('p', 'strategy-prompt', prompt));
  detail.appendChild(card);
}

// --- Promotion ledger -----------------------------------------------------------------------

function ledgerNumber(value, digits) {
  return typeof value === 'number' && isFinite(value) ? value.toFixed(digits) : '—';
}

// Win rate comes from the ledger entry when present; otherwise it is derived from the report's
// match counts (two matches per seed, so seeds * 2 decisions).
function ledgerWinRate(entry) {
  if (entry && typeof entry.winRate === 'number' && isFinite(entry.winRate)) {
    return entry.winRate.toFixed(3);
  }
  const report = (entry && entry.report) || entry;
  if (report && typeof report.challengerWins === 'number'
    && typeof report.seeds === 'number' && report.seeds > 0) {
    return (report.challengerWins / (2 * report.seeds)).toFixed(3);
  }
  return '—';
}

// One row per promotion entry, nine cells, every value written with textContent. Each cell carries
// its column label so the <=480px stacked layout can render a real label per value instead of
// clipping six invisible columns off-screen.
function renderLedger(entries) {
  const body = byId('ledger');
  clear(body);

  const appendCell = function (row, className, value, label) {
    const cell = document.createElement('td');
    if (className) {
      cell.className = className;
    }
    cell.dataset.label = label || '';
    cell.textContent = value;
    row.appendChild(cell);
    return cell;
  };

  if (!entries || entries.length === 0) {
    const row = document.createElement('tr');
    const cell = appendCell(row, 'empty-state', EMPTY_STATES.ledger);
    cell.colSpan = 9;
    body.appendChild(row);
    return;
  }

  for (const entry of entries) {
    const record = (entry && entry.report) || entry || {};
    const promoted = Boolean(entry && entry.promoted);
    const row = document.createElement('tr');

    appendCell(row, 'cell-num', typeof entry.iteration === 'number' ? String(entry.iteration) : '—',
      LEDGER_COLUMNS[0]);
    appendCell(row, 'cell-id', entry.championId || '—', LEDGER_COLUMNS[1]);
    appendCell(row, 'cell-id', entry.challengerId || '—', LEDGER_COLUMNS[2]);

    const promotedCell = document.createElement('td');
    promotedCell.dataset.label = LEDGER_COLUMNS[3];
    const pill = el('span', promoted ? 'pill pill--promoted' : 'pill pill--rejected',
      promoted ? 'Promoted' : 'Rejected');
    promotedCell.appendChild(pill);
    row.appendChild(promotedCell);

    const reasonCell = document.createElement('td');
    reasonCell.dataset.label = LEDGER_COLUMNS[4];
    reasonCell.appendChild(el('span', 'chip chip--reason', entry.reason || '—'));
    row.appendChild(reasonCell);

    appendCell(row, 'cell-num', ledgerWinRate(entry), LEDGER_COLUMNS[5]);
    appendCell(row, 'cell-num', ledgerNumber(record.meanBbPer100, 1), LEDGER_COLUMNS[6]);
    appendCell(row, 'cell-num', ledgerNumber(record.lowerBoundBbPer100, 1), LEDGER_COLUMNS[7]);
    appendCell(row, 'cell-num', typeof record.seeds === 'number' ? String(record.seeds) : '—',
      LEDGER_COLUMNS[8]);
    body.appendChild(row);
  }
}

async function loadLedger() {
  try {
    const response = await fetch('/api/ledger');
    if (!response.ok) {
      return;
    }
    const payload = await response.json();
    renderLedger(payload.entries || []);
  } catch (error) {
    setLiveMessage('Ledger request failed.');
  }
}

// --- Training -------------------------------------------------------------------------------
//
// The training HTTP surface accepts a bounded option set, runs one job at a time on the server,
// and exposes progress through a snapshot. Everything below only reads that snapshot; promotion
// is a separate POST that the server re-verifies from persisted artifacts.

async function readJsonResponse(response) {
  try {
    return await response.json();
  } catch (error) {
    return null;
  }
}

// The server binds the seed to ulong. JSON.stringify would round a 64-bit integer through Number,
// so the seed token is composed from its exact decimal text; every other field is a validated
// integer or a JSON-encoded string.
function buildTrainingBody(options) {
  return '{"seed":' + options.seed
    + ',"iterations":' + options.iterations
    + ',"trainingHands":' + options.trainingHands
    + ',"evaluationSeeds":' + options.evaluationSeeds
    + ',"evaluationHands":' + options.evaluationHands
    + ',"marginBbPer100":' + options.marginBbPer100
    + ',"minSeeds":' + options.minSeeds
    + ',"strategyId":' + JSON.stringify(options.strategyId)
    + ',"allowWeakSeed":' + (options.allowWeakSeed ? 'true' : 'false')
    + '}';
}

function readTrainingSeed() {
  const input = byId('training-seed');
  const raw = input ? input.value.trim() : '';
  if (!/^[0-9]+$/.test(raw)) {
    throw new Error('Seed must be a non-negative integer.');
  }
  // JSON numbers cannot carry leading zeros; normalize before the exact token is composed.
  const normalized = raw.replace(/^0+(?=\d)/, '');
  if (normalized.length > 20 || BigInt(normalized) > UNSIGNED_64_MAX) {
    throw new Error('Seed must fit in an unsigned 64-bit integer.');
  }
  return normalized;
}

function readTrainingInteger(inputId, min, max, label) {
  const input = byId(inputId);
  const text = input ? input.value.trim() : '';
  const value = Number(text);
  if (text.length === 0 || !Number.isSafeInteger(value) || value < min || value > max) {
    throw new Error(label + ' must be a whole number between ' + min + ' and ' + max + '.');
  }
  return value;
}

function readTrainingMargin() {
  const input = byId('training-margin');
  const text = input ? input.value.trim() : '';
  const value = Number(text);
  if (text.length === 0 || !isFinite(value) || value < 0 || value > 100) {
    throw new Error('Margin must be a number between 0 and 100 bb/100.');
  }
  return value;
}

function describeTrainingError(payload, status) {
  if (!payload || typeof payload !== 'object') {
    return 'Request failed with status ' + status + '.';
  }
  const parts = [];
  if (typeof payload.error === 'string' && payload.error.length > 0) {
    parts.push(payload.error);
  }
  if (typeof payload.field === 'string' && payload.field.length > 0) {
    parts.push('field ' + payload.field);
  }
  if (typeof payload.message === 'string' && payload.message.length > 0) {
    parts.push(payload.message);
  }
  if (typeof payload.activeJobId === 'string' && payload.activeJobId.length > 0) {
    parts.push('active job ' + payload.activeJobId);
  }
  if (parts.length === 0) {
    parts.push('Request failed with status ' + status + '.');
  }
  return parts.join(' · ');
}

function setTrainingStatus(text) {
  setText(byId('training-status'), text);
}

function setTrainingMessage(text, kind) {
  const node = byId('training-message');
  if (!node) {
    return;
  }
  const value = text === undefined || text === null ? '' : String(text);
  setText(node, value);
  const nextKind = kind || '';
  if (node.dataset.kind !== nextKind) {
    node.dataset.kind = nextKind;
  }
  node.hidden = value.length === 0;
}

function setTrainingJobActive(active) {
  const cancel = byId('training-cancel');
  if (cancel) {
    cancel.disabled = !active || state.trainingCancelRequested;
  }
}

function updateTrainingPromote(snapshot) {
  const button = byId('training-promote');
  if (!button) {
    return;
  }
  button.hidden = !(snapshot
    && snapshot.status === 'complete'
    && snapshot.canPromote === true
    && !state.trainingPromotionDone);
}

function stopTrainingPolling() {
  if (state.trainingTimer) {
    clearTimeout(state.trainingTimer);
    state.trainingTimer = null;
  }
}

function scheduleTrainingPoll() {
  stopTrainingPolling();
  state.trainingTimer = setTimeout(pollTraining, TRAINING_POLL_MS);
}

function trainingPollFailed() {
  state.trainingPollFailures += 1;
  updateTrainingPromote(state.trainingSnapshot);
  if (state.trainingPollFailures >= TRAINING_POLL_FAILURE_LIMIT) {
    stopTrainingPolling();
    setTrainingMessage('Training status polling stopped after repeated failures.', 'error');
    return;
  }
  scheduleTrainingPoll();
}

function beginTrainingJob(jobId) {
  stopTrainingPolling();
  state.trainingJobId = jobId;
  state.trainingSnapshot = null;
  state.trainingPromotionDone = false;
  state.trainingCancelRequested = false;
  state.trainingPollFailures = 0;
  state.trainingEntriesKey = null;
  renderTrainingEntries([]);
  updateTrainingPromote(null);
  setTrainingJobActive(true);
  setTrainingStatus('Queued · job ' + jobId);
  setTrainingMessage('', '');
  pollTraining();
}

async function startTraining(event) {
  event.preventDefault();
  const strategyInput = byId('training-strategy-id');
  const strategyId = strategyInput ? strategyInput.value.trim() : '';
  let body;
  try {
    body = buildTrainingBody({
      seed: readTrainingSeed(),
      iterations: readTrainingInteger('training-iterations', 1, 50, 'Iterations'),
      trainingHands: readTrainingInteger('training-hands', 1, 2000, 'Training hands'),
      evaluationSeeds: readTrainingInteger('training-eval-seeds', 2, 500, 'Evaluation seeds'),
      evaluationHands: readTrainingInteger('training-eval-hands', 1, 50, 'Evaluation hands'),
      marginBbPer100: readTrainingMargin(),
      minSeeds: readTrainingInteger('training-min-seeds', 2, 1000, 'Minimum seeds'),
      strategyId: strategyId.length > 0 ? strategyId : null,
      allowWeakSeed: byId('training-allow-weak').checked,
    });
  } catch (error) {
    setTrainingMessage(error.message || 'Training options are invalid.', 'error');
    return;
  }

  const start = byId('training-start');
  if (start) {
    start.disabled = true;
  }
  try {
    const response = await fetch('/api/training', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body,
    });
    const detail = await readJsonResponse(response);
    if (response.status === 202) {
      if (!detail || typeof detail.jobId !== 'string' || detail.jobId.length === 0) {
        setTrainingMessage('Training was accepted but no job id was returned.', 'error');
        return;
      }
      beginTrainingJob(detail.jobId);
      return;
    }
    setTrainingMessage(describeTrainingError(detail, response.status), 'error');
  } catch (error) {
    setTrainingMessage('Training start request failed.', 'error');
  } finally {
    if (start) {
      start.disabled = false;
    }
  }
}

async function cancelTraining() {
  const jobId = state.trainingJobId;
  if (!jobId) {
    return;
  }
  state.trainingCancelRequested = true;
  setTrainingJobActive(true);
  try {
    const response = await fetch('/api/training/' + encodeURIComponent(jobId), { method: 'DELETE' });
    const detail = await readJsonResponse(response);
    if (response.status === 202) {
      setTrainingMessage('Cancel requested; waiting for the job to stop.', 'info');
      return;
    }
    if (response.status === 404) {
      stopTrainingPolling();
      state.trainingJobId = null;
      state.trainingSnapshot = null;
      state.trainingEntriesKey = null;
      setTrainingJobActive(false);
      updateTrainingPromote(null);
      setTrainingStatus('No training job found.');
      setTrainingMessage('Training job is no longer available.', 'error');
      return;
    }
    // A 409 means the job already reached a terminal state; polling will surface that status.
    if (response.status !== 409) {
      state.trainingCancelRequested = false;
      setTrainingJobActive(true);
    }
    setTrainingMessage(describeTrainingError(detail, response.status), 'error');
  } catch (error) {
    state.trainingCancelRequested = false;
    setTrainingJobActive(true);
    setTrainingMessage('Cancel request failed.', 'error');
  }
}

function trainingChallengerId(snapshot) {
  if (snapshot && typeof snapshot.challengerId === 'string' && snapshot.challengerId.length > 0) {
    return snapshot.challengerId;
  }
  const entries = (snapshot && snapshot.entries) || [];
  const last = entries.length > 0 ? entries[entries.length - 1] : null;
  if (last && typeof last.challengerId === 'string' && last.challengerId.length > 0) {
    return last.challengerId;
  }
  return 'the challenger';
}

async function promoteTraining() {
  const jobId = state.trainingJobId;
  const snapshot = state.trainingSnapshot;
  if (!jobId || !snapshot || snapshot.status !== 'complete' || snapshot.canPromote !== true) {
    return;
  }
  const challengerId = trainingChallengerId(snapshot);
  if (!window.confirm('Promote ' + challengerId + '? This re-verifies the persisted report on the server.')) {
    return;
  }

  const button = byId('training-promote');
  if (button) {
    button.disabled = true;
  }
  try {
    const response = await fetch(
      '/api/training/' + encodeURIComponent(jobId) + '/promote',
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirm: true }),
      },
    );
    const detail = await readJsonResponse(response);
    if (response.ok) {
      const championId = detail && typeof detail.championId === 'string'
        ? detail.championId
        : challengerId;
      const promoted = detail && detail.status === 'already-promoted'
        ? 'Already promoted: '
        : 'Promoted ';
      setTrainingMessage(promoted + championId + ' (server verified).', 'success');
      state.trainingPromotionDone = true;
      updateTrainingPromote(snapshot);
      await loadChampion();
      return;
    }
    // A refusal is displayed verbatim and never retried automatically; the user may retry by hand.
    setTrainingMessage(describeTrainingError(detail, response.status), 'error');
    if (response.status === 404) {
      state.trainingPromotionDone = true;
      updateTrainingPromote(snapshot);
    }
    if (button) {
      button.disabled = false;
    }
  } catch (error) {
    setTrainingMessage('Promote request failed.', 'error');
    if (button) {
      button.disabled = false;
    }
  }
}

function trainingOutcome(outcome) {
  if (outcome === 'promoted') {
    return { label: 'Promoted', className: 'pill pill--promoted' };
  }
  if (outcome === 'verification-failed') {
    return { label: 'Verification failed', className: 'pill pill--rejected' };
  }
  if (outcome === 'inert-proposal') {
    return { label: 'Inert proposal', className: 'pill pill--rejected' };
  }
  if (outcome === 'rejected') {
    return { label: 'Rejected', className: 'pill pill--rejected' };
  }
  return {
    label: typeof outcome === 'string' && outcome.length > 0 ? capitalize(outcome) : '—',
    className: 'pill pill--rejected',
  };
}

function createStrategyLink(id, className) {
  const label = typeof id === 'string' && id.length > 0 ? id : '—';
  if (label === '—') {
    return el('span', className || null, label);
  }
  const link = el('a', className || null, label);
  link.setAttribute('href', '/api/strategies/' + encodeURIComponent(id));
  link.setAttribute('target', '_blank');
  link.setAttribute('rel', 'noopener');
  return link;
}

function createLedgerLink() {
  const link = el('a', 'training-ledger-link', 'Promotion ledger');
  link.setAttribute('href', '/api/ledger');
  link.setAttribute('target', '_blank');
  link.setAttribute('rel', 'noopener');
  return link;
}

function renderTrainingChampionEmpty(message) {
  const card = byId('training-champion');
  if (!card) {
    return;
  }
  clear(card);
  card.appendChild(el('p', 'empty-state', message));
  card.appendChild(createLedgerLink());
}

function renderTrainingChampion(champion) {
  if (!champion || typeof champion.strategyId !== 'string' || champion.strategyId.length === 0) {
    renderTrainingChampionEmpty('No active champion yet');
    return;
  }
  const card = byId('training-champion');
  if (!card) {
    return;
  }
  clear(card);

  const head = el('div', 'training-champion-head');
  head.appendChild(el('span', 'training-champion-label', 'Strategy'));
  head.appendChild(createStrategyLink(champion.strategyId, 'training-champion-id'));
  card.appendChild(head);

  const iteration = typeof champion.iteration === 'number' ? String(champion.iteration) : '—';
  card.appendChild(el('p', 'training-champion-meta', 'Iteration ' + iteration));

  const hash = typeof champion.evaluationReportHash === 'string' && champion.evaluationReportHash.length > 0
    ? champion.evaluationReportHash
    : '—';
  const hashLine = el('p', 'training-champion-hash');
  hashLine.appendChild(el('span', 'training-champion-hash-label', 'Report hash'));
  hashLine.appendChild(el('code', 'training-champion-hash-value', hash));
  card.appendChild(hashLine);

  card.appendChild(createLedgerLink());
}

function renderTrainingTimeline(entries) {
  const list = byId('training-timeline');
  clear(list);
  if (!entries || entries.length === 0) {
    list.appendChild(el('li', 'empty-state', 'No completed iterations yet.'));
    return;
  }
  for (const entry of entries) {
    const step = el('li', 'training-step');
    if (typeof entry.iteration === 'number') {
      step.dataset.iteration = String(entry.iteration);
    }
    const head = el('div', 'training-step-head');
    head.appendChild(el('span', 'training-step-iteration',
      'Iteration ' + (typeof entry.iteration === 'number' ? entry.iteration : '—')));
    const outcome = trainingOutcome(entry.outcome);
    head.appendChild(el('span', outcome.className, outcome.label));
    step.appendChild(head);

    const matchup = el('p', 'training-step-matchup');
    matchup.appendChild(createStrategyLink(entry.championId, 'training-step-id'));
    matchup.appendChild(el('span', 'training-step-arrow', '→'));
    matchup.appendChild(createStrategyLink(entry.challengerId, 'training-step-id'));
    step.appendChild(matchup);

    const stats = el('p', 'training-step-stats');
    stats.appendChild(el('span', 'training-step-stat',
      'Mean ' + formatNumber(entry.meanBbPer100, 1) + ' bb/100'));
    stats.appendChild(el('span', 'training-step-stat',
      'LCB ' + formatNumber(entry.lowerBoundBbPer100, 1)
      + ' vs margin ' + formatNumber(entry.marginBbPer100, 1) + ' bb/100'));
    step.appendChild(stats);

    const decision = el('p', 'training-step-decision');
    decision.appendChild(el('span', 'training-step-decision-label', 'Decision'));
    decision.appendChild(el('span', 'chip chip--reason',
      typeof entry.decision === 'string' && entry.decision.length > 0 ? entry.decision : '—'));
    if (entry.verified === true) {
      decision.appendChild(el('span', 'training-step-verified', 'Verified'));
    }
    step.appendChild(decision);

    list.appendChild(step);
  }
}

function appendTrainingCell(row, className, content, label) {
  const cell = document.createElement('td');
  if (className) {
    cell.className = className;
  }
  cell.dataset.label = label;
  if (content instanceof Node) {
    cell.appendChild(content);
  } else {
    cell.textContent = content === undefined || content === null ? '—' : String(content);
  }
  row.appendChild(cell);
}

function renderTrainingTable(entries) {
  const body = byId('training-entries');
  clear(body);
  if (!entries || entries.length === 0) {
    const row = document.createElement('tr');
    const cell = el('td', 'empty-state', 'No completed iterations yet.');
    cell.colSpan = 7;
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }
  for (const entry of entries) {
    const row = document.createElement('tr');
    appendTrainingCell(row, 'cell-num',
      typeof entry.iteration === 'number' ? String(entry.iteration) : '—', 'Iteration');
    appendTrainingCell(row, 'cell-id', createStrategyLink(entry.championId), 'Champion');
    appendTrainingCell(row, 'cell-id', createStrategyLink(entry.challengerId), 'Challenger');
    appendTrainingCell(row, 'cell-num', formatNumber(entry.meanBbPer100, 1), 'Mean bb/100');
    appendTrainingCell(row, 'cell-num',
      formatNumber(entry.lowerBoundBbPer100, 1) + ' vs ' + formatNumber(entry.marginBbPer100, 1),
      'LCB vs margin');
    const decision = el('span', 'chip chip--reason',
      typeof entry.decision === 'string' && entry.decision.length > 0 ? entry.decision : '—');
    appendTrainingCell(row, null, decision, 'Decision');
    const outcome = trainingOutcome(entry.outcome);
    appendTrainingCell(row, null, el('span', outcome.className, outcome.label), 'Outcome');
    body.appendChild(row);
  }
}

function trainingEntriesKey(entries) {
  const parts = [];
  for (const entry of entries || []) {
    parts.push([
      entry.iteration,
      entry.championId,
      entry.challengerId,
      entry.meanBbPer100,
      entry.lowerBoundBbPer100,
      entry.marginBbPer100,
      entry.seeds,
      entry.decision,
      entry.verified,
      entry.outcome,
    ].join('\u001f'));
  }
  return parts.join('\u001e');
}

// Both the timeline and the table are rebuilt only when an iteration actually changes; polling
// four times a second must not churn the DOM or re-announce unchanged steps.
function renderTrainingEntries(entries) {
  const key = trainingEntriesKey(entries);
  if (key === state.trainingEntriesKey) {
    return;
  }
  state.trainingEntriesKey = key;
  renderTrainingTimeline(entries);
  renderTrainingTable(entries);
}

function renderTrainingSnapshot(snapshot) {
  state.trainingSnapshot = snapshot;
  const status = typeof snapshot.status === 'string' ? snapshot.status : 'unknown';
  const phase = typeof snapshot.phase === 'string' ? snapshot.phase : '—';
  const iteration = typeof snapshot.iteration === 'number' ? snapshot.iteration : 0;
  const total = typeof snapshot.totalIterations === 'number' ? snapshot.totalIterations : 0;
  setTrainingStatus(capitalize(status) + ' · ' + phase + ' · iteration ' + iteration + ' of ' + total);
  renderTrainingEntries(snapshot.entries || []);
  setTrainingJobActive(status === 'queued' || status === 'running');
  updateTrainingPromote(snapshot);

  const message = byId('training-message');
  if (message && message.dataset.kind === 'poll-error') {
    setTrainingMessage('', '');
  }
  if (status === 'failed' && typeof snapshot.error === 'string' && snapshot.error.length > 0) {
    setTrainingMessage(snapshot.error, 'error');
  }
}

function isTrainingTerminal(status) {
  return status === 'complete' || status === 'failed' || status === 'cancelled';
}

async function pollTraining() {
  state.trainingTimer = null;
  const jobId = state.trainingJobId;
  if (!jobId) {
    return;
  }
  try {
    const response = await fetch('/api/training/' + encodeURIComponent(jobId));
    if (state.trainingJobId !== jobId) {
      return;
    }
    if (response.status === 404) {
      stopTrainingPolling();
      state.trainingJobId = null;
      state.trainingSnapshot = null;
      state.trainingEntriesKey = null;
      state.trainingCancelRequested = false;
      setTrainingJobActive(false);
      updateTrainingPromote(null);
      setTrainingStatus('No training job found.');
      setTrainingMessage('Training job ' + jobId + ' is no longer available.', 'error');
      return;
    }
    if (!response.ok) {
      setTrainingMessage('Training status request failed with status ' + response.status + '.',
        'poll-error');
      trainingPollFailed();
      return;
    }
    const snapshot = await response.json();
    if (state.trainingJobId !== jobId) {
      return;
    }
    state.trainingPollFailures = 0;
    renderTrainingSnapshot(snapshot);
    const status = typeof snapshot.status === 'string' ? snapshot.status : '';
    if (!isTrainingTerminal(status)) {
      scheduleTrainingPoll();
      return;
    }

    stopTrainingPolling();
    state.trainingCancelRequested = false;
    setTrainingJobActive(false);
    updateTrainingPromote(snapshot);
    if (status === 'complete') {
      // The active-champion pointer may have moved; refresh the card on the same settling poll.
      loadChampion();
    } else if (status === 'failed') {
      setTrainingMessage(
        typeof snapshot.error === 'string' && snapshot.error.length > 0
          ? snapshot.error
          : 'Training job failed.',
        'error');
    } else {
      setTrainingMessage('Training job cancelled.', 'info');
    }
  } catch (error) {
    if (state.trainingJobId !== jobId) {
      return;
    }
    setTrainingMessage('Training status request failed.', 'poll-error');
    trainingPollFailed();
  }
}

// --- View switching and keyboard shortcuts --------------------------------------------------

function selectView(view) {
  if (view !== state.view) {
    // A different panel owns the single action-log element now; force a clean rebuild.
    state.renderedActionMatch = null;
  }
  state.view = view;
  for (const tab of document.querySelectorAll('.tab')) {
    const selected = tab.dataset.view === view;
    tab.setAttribute('aria-pressed', selected ? 'true' : 'false');
    tab.classList.toggle('is-active', selected);
  }
  byId('view-table').hidden = view !== 'live' && view !== 'replay';
  byId('view-strategies').hidden = view !== 'strategies';
  byId('view-ledger').hidden = view !== 'ledger';
  byId('view-training').hidden = view !== 'training';

  if (view === 'live') {
    if (state.liveSnapshot) {
      renderLive(state.liveSnapshot);
    } else {
      showTableEmpty();
    }
  } else if (view === 'replay') {
    if (state.replayHand) {
      renderReplay(state.replayHand);
    } else {
      showTableEmpty();
    }
  }
}

// Space must activate a focused button (and arrows must not scrub the replay) while any interactive
// control owns focus; the global shortcuts apply only when focus is on the page itself.
function isInteractiveTarget(target) {
  if (!target || !target.tagName) {
    return false;
  }
  if (target.isContentEditable === true) {
    return true;
  }
  return Boolean(target.closest && target.closest(
    'button, a[href], input, select, textarea, [contenteditable="true"], [role="tab"], [role="button"], [role="slider"]',
  ));
}

function handleKeydown(event) {
  if (isInteractiveTarget(event.target)) {
    return;
  }
  if (event.key === ' ' || event.code === 'Space') {
    event.preventDefault();
    togglePause();
    return;
  }
  if (event.key === 'ArrowLeft') {
    event.preventDefault();
    stepReplayHand(-1);
    return;
  }
  if (event.key === 'ArrowRight') {
    event.preventDefault();
    stepReplayHand(1);
    return;
  }
  const view = VIEW_KEYS[event.key];
  if (view) {
    event.preventDefault();
    selectView(view);
  }
}

// --- Mini-RSI (proposer policy) -------------------------------------------------------------

function readRsiSeed() {
  const input = byId('rsi-seed');
  const raw = input ? input.value.trim() : '';
  if (!/^[0-9]+$/.test(raw)) {
    throw new Error('Seed must be a non-negative integer.');
  }
  const normalized = raw.replace(/^0+(?=\d)/, '');
  if (normalized.length > 20 || BigInt(normalized) > UNSIGNED_64_MAX) {
    throw new Error('Seed must fit in an unsigned 64-bit integer.');
  }
  return normalized;
}

function readRsiMargin() {
  const input = byId('rsi-margin');
  const text = input ? input.value.trim() : '';
  const value = Number(text);
  if (text.length === 0 || !isFinite(value) || value < 0 || value > 100) {
    throw new Error('Margin must be a number between 0 and 100 bb/100.');
  }
  return value;
}

function buildRsiBody(options) {
  return '{"seed":' + options.seed
    + ',"metaIterations":' + options.metaIterations
    + ',"innerIterations":' + options.innerIterations
    + ',"metaSeeds":' + options.metaSeeds
    + ',"evaluationSeeds":' + options.evaluationSeeds
    + ',"marginBbPer100":' + options.marginBbPer100
    + ',"minSeeds":' + options.minSeeds
    + ',"allowWeakSeed":false'
    + '}';
}

function setRsiStatus(text) {
  setText(byId('rsi-status'), text);
}

function setRsiMessage(text, kind) {
  const node = byId('rsi-message');
  if (!node) {
    return;
  }
  const value = text === undefined || text === null ? '' : String(text);
  setText(node, value);
  const nextKind = kind || '';
  if (node.dataset.kind !== nextKind) {
    node.dataset.kind = nextKind;
  }
  node.hidden = value.length === 0;
}

function setRsiActive(active) {
  const cancel = byId('rsi-cancel');
  if (cancel) {
    cancel.disabled = !active || state.rsiCancelRequested;
  }
}

function stopRsiPolling() {
  if (state.rsiTimer) {
    clearTimeout(state.rsiTimer);
    state.rsiTimer = null;
  }
}

function scheduleRsiPoll() {
  stopRsiPolling();
  state.rsiTimer = setTimeout(pollRsi, TRAINING_POLL_MS);
}

function updateRsiPromote(snapshot) {
  const button = byId('rsi-promote');
  if (!button) {
    return;
  }
  button.hidden = !(snapshot
    && snapshot.status === 'complete'
    && snapshot.canPromote === true
    && !state.rsiPromotionDone);
}

function renderRsiSnapshot(snapshot) {
  if (!snapshot) {
    return;
  }
  const status = typeof snapshot.status === 'string' ? snapshot.status : 'unknown';
  const phase = typeof snapshot.phase === 'string' ? snapshot.phase : '—';
  onTextStatus(status, phase, snapshot);
  updateRsiPromote(snapshot);

  if (status === 'complete') {
    setRsiActive(false);
    if (snapshot.eligibleProposerId) {
      setRsiMessage(
        'Eligible proposer ' + snapshot.eligibleProposerId
          + '. The server re-verifies the persisted document before applying it.',
        'success');
    } else {
      const reason = snapshot.lastReport && snapshot.lastReport.decision
        ? snapshot.lastReport.decision
        : 'no eligible proposer';
      setRsiMessage('Run complete: ' + reason + '. The strategy champion is unchanged.', 'info');
    }
    return;
  }
  if (status === 'failed' || status === 'cancelled') {
    setRsiActive(false);
    const suffix = snapshot.error ? ' (' + snapshot.error + ')' : '';
    setRsiMessage('Mini-RSI ' + status + suffix + '.', status === 'failed' ? 'error' : 'info');
    return;
  }
  setRsiActive(true);
  setRsiMessage('', '');
}

function onTextStatus(status, phase, snapshot) {
  const iteration = typeof snapshot.iteration === 'number' ? snapshot.iteration : 0;
  const total = typeof snapshot.totalIterations === 'number' ? snapshot.totalIterations : 0;
  const parts = [status, phase, 'iteration ' + iteration + ' of ' + total];
  if (snapshot.incumbentProposerId) {
    parts.push('incumbent ' + snapshot.incumbentProposerId);
  }
  if (snapshot.lastReport && snapshot.lastReport.decision) {
    parts.push(snapshot.lastReport.decision);
  }
  setRsiStatus(parts.join(' · '));
}

async function pollRsi() {
  if (!state.rsiJobId) {
    return;
  }
  try {
    const response = await fetch('/api/rsi/' + encodeURIComponent(state.rsiJobId));
    const payload = await readJsonResponse(response);
    if (!response.ok) {
      state.rsiPollFailures += 1;
      if (state.rsiPollFailures >= TRAINING_POLL_FAILURE_LIMIT) {
        stopRsiPolling();
        setRsiMessage(describeTrainingError(payload, response.status), 'error');
        return;
      }
      scheduleRsiPoll();
      return;
    }
    state.rsiPollFailures = 0;
    state.rsiSnapshot = payload;
    renderRsiSnapshot(payload);
    if (payload.status === 'complete' || payload.status === 'failed' || payload.status === 'cancelled') {
      stopRsiPolling();
      return;
    }
    scheduleRsiPoll();
  } catch (error) {
    state.rsiPollFailures += 1;
    if (state.rsiPollFailures >= TRAINING_POLL_FAILURE_LIMIT) {
      stopRsiPolling();
      setRsiMessage('Mini-RSI status polling stopped after repeated failures.', 'error');
      return;
    }
    scheduleRsiPoll();
  }
}

async function startRsi(event) {
  event.preventDefault();
  let body;
  try {
    body = buildRsiBody({
      seed: readRsiSeed(),
      metaIterations: readTrainingInteger('rsi-meta-iterations', 1, 8, 'Meta iterations'),
      innerIterations: readTrainingInteger('rsi-inner-iterations', 1, 5, 'Inner iterations'),
      metaSeeds: readTrainingInteger('rsi-meta-seeds', 2, 50, 'Meta seeds'),
      evaluationSeeds: readTrainingInteger('rsi-eval-seeds', 2, 50, 'Evaluation seeds'),
      marginBbPer100: readRsiMargin(),
      minSeeds: readTrainingInteger('rsi-min-seeds', 2, 1000, 'Minimum seeds'),
    });
  } catch (error) {
    setRsiMessage(error.message || 'Mini-RSI options are invalid.', 'error');
    return;
  }

  const start = byId('rsi-start');
  if (start) {
    start.disabled = true;
  }
  try {
    const response = await fetch('/api/rsi', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body,
    });
    const detail = await readJsonResponse(response);
    if (response.status === 202 && detail && typeof detail.jobId === 'string') {
      stopRsiPolling();
      state.rsiJobId = detail.jobId;
      state.rsiSnapshot = null;
      state.rsiCancelRequested = false;
      state.rsiPollFailures = 0;
      state.rsiPromotionDone = false;
      updateRsiPromote(null);
      setRsiActive(true);
      setRsiStatus('Queued · job ' + detail.jobId);
      setRsiMessage('', '');
      pollRsi();
      return;
    }
    setRsiMessage(describeTrainingError(detail, response.status), 'error');
  } catch (error) {
    setRsiMessage('Mini-RSI start request failed.', 'error');
  } finally {
    if (start) {
      start.disabled = false;
    }
  }
}

async function cancelRsi() {
  const jobId = state.rsiJobId;
  if (!jobId) {
    return;
  }
  state.rsiCancelRequested = true;
  setRsiActive(true);
  try {
    const response = await fetch('/api/rsi/' + encodeURIComponent(jobId), { method: 'DELETE' });
    const detail = await readJsonResponse(response);
    if (response.status === 202) {
      setRsiMessage('Cancel requested; waiting for the job to stop.', 'info');
      return;
    }
    if (response.status !== 409) {
      state.rsiCancelRequested = false;
      setRsiActive(true);
    }
    setRsiMessage(describeTrainingError(detail, response.status), 'error');
  } catch (error) {
    state.rsiCancelRequested = false;
    setRsiActive(true);
    setRsiMessage('Mini-RSI cancel request failed.', 'error');
  }
}

async function promoteRsi() {
  const jobId = state.rsiJobId;
  const snapshot = state.rsiSnapshot;
  if (!jobId || !snapshot || snapshot.status !== 'complete' || snapshot.canPromote !== true) {
    return;
  }
  const proposerId = snapshot.eligibleProposerId || 'the eligible proposer';
  if (!window.confirm('Promote proposer ' + proposerId
    + '? This re-verifies the persisted meta document and the frozen manifest on the server.')) {
    return;
  }
  const button = byId('rsi-promote');
  if (button) {
    button.disabled = true;
  }
  try {
    const response = await fetch(
      '/api/rsi/' + encodeURIComponent(jobId) + '/promote',
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirm: true }),
      },
    );
    const detail = await readJsonResponse(response);
    if (response.ok) {
      setRsiMessage('Proposer promoted and server verified: ' + (detail.proposerId || proposerId) + '.', 'success');
      state.rsiPromotionDone = true;
      updateRsiPromote(snapshot);
      return;
    }
    setRsiMessage(describeTrainingError(detail, response.status), 'error');
    if (button) {
      button.disabled = false;
    }
  } catch (error) {
    setRsiMessage('Mini-RSI promote request failed.', 'error');
    if (button) {
      button.disabled = false;
    }
  }
}

function initialize() {
  byId('seed').value = randomSeedText();
  byId('training-seed').value = randomSeedText();
  byId('rsi-seed').value = randomSeedText();
  byId('live-form').addEventListener('submit', startLiveMatch);
  byId('training-form').addEventListener('submit', startTraining);
  byId('training-cancel').addEventListener('click', cancelTraining);
  byId('training-promote').addEventListener('click', promoteTraining);
  byId('rsi-form').addEventListener('submit', startRsi);
  byId('rsi-cancel').addEventListener('click', cancelRsi);
  byId('rsi-promote').addEventListener('click', promoteRsi);
  byId('pause-live').addEventListener('click', function () {
    sendLiveControl('/pause', 'POST');
  });
  byId('resume-live').addEventListener('click', function () {
    sendLiveControl('/resume', 'POST');
  });
  byId('stop-live').addEventListener('click', function () {
    sendLiveControl('', 'DELETE');
  });
  byId('poll-speed').addEventListener('change', function (event) {
    state.pollSpeed = Number(event.target.value);
  });
  byId('replay-scrubber').addEventListener('input', function (event) {
    selectView('replay');
    loadReplayHand(Number(event.target.value));
  });
  for (const tab of document.querySelectorAll('.tab')) {
    tab.addEventListener('click', function () {
      selectView(tab.dataset.view);
    });
  }
  document.addEventListener('keydown', handleKeydown);

  selectView('live');
  refreshStatus();
  loadSessions();
  loadStrategies();
  loadLedger();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initialize);
} else {
  initialize();
}
