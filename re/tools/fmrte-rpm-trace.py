"""Trace FMRTE's reads/writes of one FM process; does not inject into FM.

Examples:
  python fmrte-rpm-trace.py --fmrte-pid 123 --fm-pid 456 --log trace.jsonl
  python fmrte-rpm-trace.py --fmrte-pid 123 --fm-pid 456 --log trace.jsonl --target 0x1234

With targets, retain the preceding reads in a bounded ring and flush it when a read or write overlaps a target.
Without targets, capture only the first --sample reads plus all small writes. Addresses are session-specific.
"""
import argparse
import json
import sys
import threading
import time

import frida


def parse_address(text):
    return int(text, 0)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--fmrte-pid", type=int, required=True)
parser.add_argument("--fm-pid", type=int, required=True)
parser.add_argument("--log", required=True)
parser.add_argument("--target", action="append", type=parse_address, default=[])
parser.add_argument("--ring", type=int, default=400)
parser.add_argument("--sample", type=int, default=20)
parser.add_argument("--seconds", type=int, default=30)
args = parser.parse_args()
if args.fmrte_pid <= 0 or args.fm_pid <= 0 or args.fmrte_pid == args.fm_pid:
    parser.error("FMRTE and FM must be distinct positive process IDs")
if not 1 <= args.ring <= 10000 or not 0 <= args.sample <= 500000 or not 1 <= args.seconds <= 600:
    parser.error("ring, sample or seconds outside allowed range")

config = json.dumps({"fmPid": args.fm_pid, "targets": [hex(x) for x in args.target], "ring": args.ring, "sample": args.sample})
source = r"""
const cfg = CONFIG;
const k = Process.getModuleByName('kernel32.dll');
const processId = new NativeFunction(k.getExportByName('GetProcessId'), 'uint', ['pointer']);
const targets = cfg.targets.map(ptr);
let recent = [], sampled = 0;
function overlaps(addr, size) {
  if (size === 0) return false;
  const end = addr.add(size);
  return targets.some(t => addr.compare(t) <= 0 && t.compare(end) < 0);
}
function hexData(buffer, count) {
  if (count === 0 || count > 64) return null;
  try { return Array.from(new Uint8Array(buffer.readByteArray(count))).map(b => ('0' + b.toString(16)).slice(-2)).join(''); }
  catch (_) { return null; }
}
function record(kind, addr, size, data, success) {
  const entry = {kind, address: addr.toString(), size, data, success};
  if (overlaps(addr, size)) { send({event: 'target', previous: recent, hit: entry}); recent = []; }
  else if (kind === 'write' || (targets.length === 0 && sampled++ < cfg.sample)) send({event: kind, entry});
  if (kind === 'read') { recent.push(entry); if (recent.length > cfg.ring) recent.shift(); }
}
Interceptor.attach(k.getExportByName('ReadProcessMemory'), {
  onEnter(args) { this.ok = processId(args[0]) === cfg.fmPid; if (this.ok) { this.addr = args[1]; this.buf = args[2]; this.size = args[3].toUInt32(); } },
  onLeave(result) { if (this.ok) record('read', this.addr, this.size, result.isNull() ? null : hexData(this.buf, this.size), !result.isNull()); }
});
Interceptor.attach(k.getExportByName('WriteProcessMemory'), {
  onEnter(args) { this.ok = processId(args[0]) === cfg.fmPid; if (this.ok) { this.addr = args[1]; this.size = args[3].toUInt32(); this.data = hexData(args[2], this.size); } },
  onLeave(result) { if (this.ok) record('write', this.addr, this.size, this.data, !result.isNull()); }
});
send({event: 'ready', fmrtePid: Process.id, fmPid: cfg.fmPid});
""".replace("CONFIG", config, 1)

with open(args.log, "x", encoding="utf-8", buffering=1) as log:
    session = frida.attach(args.fmrte_pid)
    script = None
    ready = threading.Event()
    script_errors = []
    try:
        script = session.create_script(source)

        def on_message(message, data):
            log.write(json.dumps(message, ensure_ascii=False) + "\n")
            if message.get("type") == "error":
                script_errors.append(message)
                ready.set()
                print(json.dumps(message, ensure_ascii=False), file=sys.stderr, flush=True)
            elif message.get("payload", {}).get("event") == "ready":
                ready.set()

        script.on("message", on_message)
        script.load()
        if not ready.wait(3) or script_errors:
            raise RuntimeError("Frida hooks did not become ready; inspect the log")
        print(f"Attached to FMRTE {args.fmrte_pid}, watching FM {args.fm_pid}; log: {args.log}", flush=True)
        time.sleep(args.seconds)
    except KeyboardInterrupt:
        pass
    finally:
        try:
            if script is not None:
                script.unload()
        finally:
            session.detach()
