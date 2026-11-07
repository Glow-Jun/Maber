const app = document.querySelector<HTMLDivElement>("#app")!;
app.textContent = "Maber kernel booting…";
window.chrome?.webview?.postMessage(JSON.stringify({ type: "ready" }));
