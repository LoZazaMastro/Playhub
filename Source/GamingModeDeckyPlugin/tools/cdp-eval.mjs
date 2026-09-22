const titleArgument = process.argv.find((value) => value.startsWith("--title="));
const targetTitle = titleArgument?.slice("--title=".length) || "SharedJSContext";
const expression = process.argv.slice(2).filter((value) => value !== titleArgument).join(" ");
if (!expression) throw new Error("Missing JavaScript expression");

const targets = await (await fetch("http://127.0.0.1:8080/json")).json();
const target = targets.find((candidate) => candidate.title === targetTitle);
if (!target) throw new Error(`${targetTitle} not found`);

const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
  socket.addEventListener("open", resolve, { once: true });
  socket.addEventListener("error", reject, { once: true });
});

socket.send(JSON.stringify({ id: 1, method: "Runtime.evaluate", params: {
  expression, returnByValue: true, awaitPromise: true
} }));
const result = await new Promise((resolve, reject) => {
  const timeout = setTimeout(() => reject(new Error("CDP timeout")), 60000);
  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.id !== 1) return;
    clearTimeout(timeout);
    resolve(message);
  });
});
socket.close();
console.log(JSON.stringify(result));
