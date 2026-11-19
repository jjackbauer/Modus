import init, { hydrate } from "/pkg/control_plane_web.js";

const boot = document.getElementById("boot-error");

try {
  await init();
  hydrate();
  document.documentElement.dataset.hydrated = "true";
} catch (error) {
  console.error("control plane hydration failed", error);
  if (boot) {
    boot.hidden = false;
    boot.textContent =
      "The control plane client failed to start. Refresh the page. If this persists, rebuild the WebAssembly bundle.";
  }
  document.documentElement.dataset.hydrated = "false";
}
