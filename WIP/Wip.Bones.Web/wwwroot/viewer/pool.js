(function () {
  "use strict";
  var POLL_MS = 5000;
  var STATUS_POLL_MS = 2000;
  var ROOT = document.getElementById("pool-dashboard");
  if (!ROOT) { console.warn("pool.js: #pool-dashboard not found"); return; }

  var RUNNING = ROOT.dataset.isRunning === "true";
  var POLL_ID = null;
  var STATUS_POLL_ID = null;
  var CARDS = document.querySelectorAll(".pool-card");

  function api(path) { return fetch("/bones" + path).then(function(r) { return r.ok ? r.json() : null; }); }

  function toggleCard(el) {
    var body = el.querySelector(".pool-card-detail");
    if (!body) return;
    var open = el.classList.contains("expanded");
    el.classList.toggle("expanded", !open);
    if (!open && !body.dataset.loaded) {
      body.dataset.loaded = "true";
      body.classList.remove("hidden");
      var id = el.dataset.strategyId;
      if (id) {
        api("/pool/" + encodeURIComponent(id)).then(function(d) {
          if (!d) return;
          var src = body.querySelector(".pool-card-source code");
          if (src) src.textContent = d.source;
          var links = body.querySelector(".pool-card-match-links");
          if (links && d.recentMatches) {
            var h = "";
            d.recentMatches.forEach(function(m) {
              h += "<a href=\"/bones/sessions/" + m.sessionId + "/matches/" + m.matchId + "/view\" target=\"_blank\">Match " + m.matchId + "</a> ";
            });
            links.innerHTML = h || "<span>No match replays available</span>";
          }
          var lin = body.querySelector(".pool-card-lineage");
          if (lin && d.lineage) {
            var l = d.lineage;
            var h = "<div>Predecessor: " + (l.predecessorId || "none") + "</div>";
            h += "<div>Outcome: " + l.outcome + "</div>";
            h += "<div>Diff: " + l.diffSummary + "</div>";
            h += "<div>Successors: " + (l.successorIds.length ? l.successorIds.join(", ") : "none") + "</div>";
            lin.innerHTML = h;
          }
          var opp = body.querySelector(".pool-card-opponents tbody");
          if (opp && d.opponentSummaries) {
            var h = "";
            d.opponentSummaries.forEach(function(o) {
              h += "<tr><td>" + o.opponentStrategyId + "</td><td>" + o.opponentKind + "</td><td>" + o.opponentElo.toFixed(0) + "</td><td>" + o.gamesAgainst + "</td><td>" + o.winsAgainst + "</td><td>" + o.lossesAgainst + "</td></tr>";
            });
            opp.innerHTML = h || "<tr><td colspan=\"6\">No opponent data</td></tr>";
          }
        });
      }
    }
  }

  // Attach click handler to pool-card-grid via delegation so new cards also work
  var grid = document.querySelector(".pool-card-grid");
  if (grid) {
    grid.addEventListener("click", function(e) {
      // Let anchor clicks navigate, don't interfere
      if (e.target.closest("a")) return;
      var card = e.target.closest(".pool-card");
      if (!card) return;
      toggleCard(card);
    });
  }

  // Also bind to existing cards for SSR initial state
  CARDS.forEach(function(card) {
    card.addEventListener("click", function(e) {
      if (e.target.closest("a")) return;
      toggleCard(card);
    });
  });

  function pollPool() {
    api("/pool").then(function(snap) {
      if (!snap || !snap.entries) return;
      refreshCards(snap.entries);
      // Update running state from snapshot
      RUNNING = snap.isRunning === true;
      ROOT.dataset.isRunning = String(RUNNING);
    });
  }

  function pollLearningLoopStatus() {
    fetch("/api/bones/status").then(function(r) {
      if (!r.ok) return;
      return r.json();
    }).then(function(status) {
      if (!status) return;
      updateStatusBar(status);
    }).catch(function() { /* transient */ });
  }

  function updateStatusBar(status) {
    var panel = document.getElementById("learning-loop-status");
    if (!panel) return;

    var isRunning = status.isRunning === true;
    panel.dataset.isRunning = String(isRunning);
    panel.dataset.iterationCount = String(status.iterationCount || 0);
    panel.dataset.currentStage = status.currentStage || "";

    var runningEl = document.getElementById("loop-running-value");
    if (runningEl) {
      runningEl.textContent = status.isRunning ? "Running" : (status.capReached ? "Stopped (cap reached)" : "Stopped");
    }
    var iterEl = document.getElementById("loop-iteration-value");
    if (iterEl) {
      iterEl.textContent = "Iteration " + (status.iterationCount || 0);
    }
    var stageEl = document.getElementById("loop-stage-value");
    if (stageEl) {
      stageEl.textContent = status.currentStage || "Idle";
    }
  }

  function refreshCards(entries) {
    var grid = document.querySelector(".pool-card-grid");
    if (!grid) return;
    var ids = entries.map(function(e) { return e.id; });
    var existing = grid.querySelectorAll(".pool-card");
    existing.forEach(function(card) {
      if (ids.indexOf(card.dataset.strategyId) === -1) {
        card.remove();
      }
    });
    entries.forEach(function(entry) {
      var existingCard = grid.querySelector('.pool-card[data-strategy-id="' + entry.id + '"]');
      if (existingCard) {
        // Update in-place stats
        var stats = existingCard.querySelectorAll(".stat-value");
        if (stats.length >= 6) {
          stats[0].textContent = entry.rating.toFixed(0);
          stats[1].textContent = (entry.winRate * 100).toFixed(1) + "%";
          stats[2].textContent = entry.matchesPlayed;
          stats[3].textContent = entry.wins + "/" + entry.losses;
          stats[4].textContent = (entry.cumulativeScoreDifferential >= 0 ? "+" : "") + entry.cumulativeScoreDifferential;
          stats[5].textContent = entry.iterationCount;
        }
      }
    });
  }

  function startPolling() {
    pollPool();
    pollLearningLoopStatus();
    POLL_ID = setInterval(pollPool, POLL_MS);
    STATUS_POLL_ID = setInterval(pollLearningLoopStatus, STATUS_POLL_MS);
  }

  function stopPolling() {
    if (POLL_ID) { clearInterval(POLL_ID); POLL_ID = null; }
    if (STATUS_POLL_ID) { clearInterval(STATUS_POLL_ID); STATUS_POLL_ID = null; }
  }

  startPolling();

  window.addEventListener("beforeunload", stopPolling);
})();
