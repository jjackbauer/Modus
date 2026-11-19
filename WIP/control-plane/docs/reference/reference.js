/*
 * Progressive enhancement for the PR 0 reference mockups.
 * Everything here is local to the static reference: theme, mobile drawer, digest copy and the
 * typed-digest affordance. No request is made and no governed action is performed.
 */
(function () {
  'use strict';

  var root = document.documentElement;
  var THEME_KEY = 'cp_ref_theme';

  function setTheme(theme, persist) {
    root.setAttribute('data-theme', theme);
    var toggle = document.querySelector('[data-theme-toggle]');
    if (toggle) {
      var next = theme === 'light' ? 'dark' : 'light';
      toggle.textContent = next === 'light' ? 'Light theme' : 'Dark theme';
      toggle.setAttribute('aria-label', 'Switch to ' + next + ' theme');
      toggle.setAttribute('aria-pressed', theme === 'light' ? 'true' : 'false');
    }
    if (persist) {
      try { localStorage.setItem(THEME_KEY, theme); } catch (error) { /* storage may be unavailable */ }
    }
  }

  var params = new URLSearchParams(window.location.search);
  var initial = params.get('theme');
  if (initial !== 'light' && initial !== 'dark') {
    try { initial = localStorage.getItem(THEME_KEY); } catch (error) { initial = null; }
  }
  setTheme(initial === 'light' ? 'light' : 'dark', false);

  document.addEventListener('click', function (event) {
    var toggle = event.target.closest && event.target.closest('[data-theme-toggle]');
    if (toggle) {
      setTheme(root.getAttribute('data-theme') === 'light' ? 'dark' : 'light', true);
    }
  });

  /* ---------------------------------------------------------------- drawer */
  var app = document.querySelector('.app');
  var rail = document.querySelector('.rail');
  var navToggle = document.querySelector('[data-nav-toggle]');
  var backdrop = document.querySelector('[data-nav-close]');
  var lastFocus = null;

  function drawerIsOpen() {
    return app && app.classList.contains('drawer-open');
  }

  function closeDrawer(restoreFocus) {
    if (!app || !drawerIsOpen()) return;
    app.classList.remove('drawer-open');
    document.body.classList.remove('drawer-open');
    if (backdrop) backdrop.hidden = true;
    if (navToggle) navToggle.setAttribute('aria-expanded', 'false');
    if (restoreFocus && navToggle) navToggle.focus();
    lastFocus = null;
  }

  function openDrawer() {
    if (!app || !rail || !navToggle) return;
    lastFocus = document.activeElement;
    app.classList.add('drawer-open');
    document.body.classList.add('drawer-open');
    if (backdrop) backdrop.hidden = false;
    navToggle.setAttribute('aria-expanded', 'true');
    var first = rail.querySelector('a, button');
    if (first) first.focus();
  }

  if (navToggle) {
    navToggle.addEventListener('click', function () {
      if (drawerIsOpen()) closeDrawer(true);
      else openDrawer();
    });
  }
  if (backdrop) {
    backdrop.addEventListener('click', function () { closeDrawer(true); });
  }
  if (rail) {
    rail.addEventListener('click', function (event) {
      if (event.target.closest('a')) closeDrawer(false);
    });
    rail.addEventListener('keydown', function (event) {
      if (!drawerIsOpen()) return;
      if (event.key === 'Escape') {
        event.preventDefault();
        closeDrawer(true);
        return;
      }
      if (event.key !== 'Tab') return;
      var items = Array.prototype.filter.call(
        rail.querySelectorAll('a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])'),
        function (item) { return item.offsetParent !== null; }
      );
      if (items.length === 0) return;
      var first = items[0];
      var last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    });
  }
  window.addEventListener('resize', function () {
    if (window.innerWidth > 1000) closeDrawer(false);
  });

  /* ------------------------------------------------------------ digest copy */
  document.addEventListener('click', function (event) {
    var button = event.target.closest && event.target.closest('[data-copy]');
    if (!button) return;
    var row = button.closest('.digest-row');
    var target = row && row.querySelector('.digest-value');
    var value = button.getAttribute('data-copy') || (target ? target.textContent.trim() : '');
    var label = button.textContent;
    var finish = function (text) {
      button.textContent = text;
      setTimeout(function () { button.textContent = label; }, 1600);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(value).then(function () { finish('Copied'); }, function () { finish('Select to copy'); });
    } else {
      finish('Select to copy');
    }
  });

  /* ---------------------------------------------------------------- filters */
  var filterToggle = document.querySelector('[data-filter-toggle]');
  var filterPanel = document.getElementById('queue-filters');
  if (filterToggle && filterPanel) {
    filterToggle.addEventListener('click', function () {
      var open = filterPanel.classList.toggle('open');
      filterToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
  }

  /* ------------------------------------------------------ view switch (mock) */
  var segmented = document.querySelector('[data-view-switch]');
  if (segmented) {
    segmented.addEventListener('click', function (event) {
      var button = event.target.closest('button[data-view]');
      if (!button) return;
      segmented.querySelectorAll('button[data-view]').forEach(function (item) {
        item.setAttribute('aria-pressed', item === button ? 'true' : 'false');
      });
      var note = document.getElementById('view-note');
      if (note) {
        note.textContent = button.dataset.view === 'table'
          ? 'Table view shown. Board and Priority reuse this toolbar and the same filtered rows.'
          : 'This static reference renders the Table view; the ' + button.dataset.view + ' view is specified in interaction-inventory.md (B-02).';
      }
    });
  }

  /* ------------------------------------------------------- typed digest mock */
  var digestInput = document.getElementById('confirm-review-digest');
  var authorize = document.getElementById('authorize-merge');
  var expected = authorize ? authorize.getAttribute('data-expected') : null;
  if (digestInput && authorize && expected) {
    var sync = function () {
      var matches = digestInput.value.trim() === expected;
      authorize.disabled = !matches;
      authorize.setAttribute('aria-disabled', matches ? 'false' : 'true');
      var reason = document.getElementById('digest-reason');
      if (reason) {
        reason.textContent = matches
          ? 'The digest matches. In the application this enables the governed mutation; this reference does not submit it.'
          : 'The authorization stays disabled until the typed value matches the exact review digest above.';
      }
    };
    digestInput.addEventListener('input', sync);
    sync();
  }
})();